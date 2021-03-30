﻿using Digimezzo.Foundation.Core.Logging;
using Digimezzo.Foundation.Core.Settings;
using Dopamine.Core.Base;
using Dopamine.Core.Extensions;
using Dopamine.Core.IO;
using Dopamine.Data;
using Dopamine.Data.Entities;
using Dopamine.Data.Metadata;
using Dopamine.Data.Repositories;
using Dopamine.Services.Cache;
using Dopamine.Services.InfoDownload;
using Dopamine.Services.Lifetime;
using SQLite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Indexing
{
    public class IndexingService : IIndexingService
    {
        // Services
        private ICacheService cacheService;
        private IInfoDownloadService infoDownloadService;
        private ITerminationService cancellationService;

        // Repositories
        private ITrackRepository trackRepository;
        private IAlbumArtworkRepository albumArtworkRepository;
        private IFolderRepository folderRepository;

        // Factories
        private ISQLiteConnectionFactory sqliteConnectionFactory;

        // Watcher
        private FolderWatcherManager watcherManager;

        // Paths
        private List<FolderPathInfo> allDiskPaths;
        private List<FolderPathInfo> newDiskPaths;

        // Cache
        private IndexerCache cache;

        // Flags
        private bool isIndexing;
        private bool isFoldersChanged;
        private bool canIndexArtwork;
        private bool isIndexingArtwork;

        // Events
        public event EventHandler IndexingStopped = delegate { };
        public event EventHandler IndexingStarted = delegate { };
        public event Action<IndexingStatusEventArgs> IndexingStatusChanged = delegate { };
        public event EventHandler RefreshLists = delegate { };
        public event EventHandler RefreshArtwork = delegate { };
        public event AlbumArtworkAddedEventHandler AlbumArtworkAdded = delegate { };

        public bool IsIndexing
        {
            get { return this.isIndexing; }
        }

        public IndexingService(
            ISQLiteConnectionFactory factory,
            ICacheService cacheService,
            IInfoDownloadService infoDownloadService,
            ITerminationService cancellationService,
            ITrackRepository trackRepository,
            IFolderRepository folderRepository,
            IAlbumArtworkRepository albumArtworkRepository)
        {
            this.cacheService = cacheService;
            this.infoDownloadService = infoDownloadService;
            this.cancellationService = cancellationService;

            this.trackRepository = trackRepository;
            this.folderRepository = folderRepository;
            this.albumArtworkRepository = albumArtworkRepository;
            this.sqliteConnectionFactory = factory;

            this.watcherManager = new FolderWatcherManager(this.folderRepository);
            this.cache = new IndexerCache(this.sqliteConnectionFactory);

            SettingsClient.SettingChanged += SettingsClient_SettingChanged;
            this.watcherManager.FoldersChanged += WatcherManager_FoldersChanged;

            this.isIndexing = false;
        }

        private async void SettingsClient_SettingChanged(object sender, SettingChangedEventArgs e)
        {
            if (SettingsClient.IsSettingChanged(e, "Indexing", "RefreshCollectionAutomatically"))
            {
                if ((bool)e.Entry.Value)
                {
                    await this.watcherManager.StartWatchingAsync();
                }
                else
                {
                    await this.watcherManager.StopWatchingAsync();
                }
            }
        }

        public async void OnFoldersChanged()
        {
            this.isFoldersChanged = true;

            if (SettingsClient.Get<bool>("Indexing", "RefreshCollectionAutomatically"))
            {
                await this.watcherManager.StartWatchingAsync();
            }
        }

        public async Task RefreshCollectionAsync()
        {
            if (!SettingsClient.Get<bool>("Indexing", "RefreshCollectionAutomatically"))
            {
                return;
            }

            await this.CheckCollectionAsync(false);
        }

        public async void RefreshCollectionIfFoldersChangedAsync()
        {
            if (!this.isFoldersChanged)
            {
                return;
            }

            this.isFoldersChanged = false;
            await this.RefreshCollectionAsync();
        }

        public async Task RefreshCollectionImmediatelyAsync()
        {
            await this.CheckCollectionAsync(true);
        }

        private async Task CheckCollectionAsync(bool forceIndexing)
        {
            if (this.IsIndexing)
            {
                return;
            }

            LogClient.Info("+++ STARTED CHECKING COLLECTION +++");

            this.canIndexArtwork = false;

            // Wait until artwork indexing is stopped
            while (this.isIndexingArtwork)
            {
                await Task.Delay(100);
            }

            await this.watcherManager.StopWatchingAsync();

            try
            {
                this.allDiskPaths = await this.GetFolderPaths();

                using (var conn = this.sqliteConnectionFactory.GetConnection())
                {
                    bool performIndexing = false;

                    if (forceIndexing)
                    {
                        performIndexing = true;
                    }
                    else
                    {
                        long databaseNeedsIndexingCount = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM Track WHERE NeedsIndexing = 1");
                        long databaseTrackCount = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM Track");
                        long databaseLastDateFileModified = conn.ExecuteScalar<long>("SELECT DateFileModified FROM Track ORDER BY DateFileModified DESC LIMIT 1");
                        long diskLastDateFileModified = this.allDiskPaths.Count > 0 ? this.allDiskPaths.Max(t => t.DateModifiedTicks) : 0;

                        performIndexing = databaseNeedsIndexingCount > 0 ||
                                          databaseTrackCount != this.allDiskPaths.Count ||
                                          databaseLastDateFileModified < diskLastDateFileModified;
                    }

                    if (performIndexing)
                    {
                        await this.IndexCollectionAsync();
                    }
                    else
                    {
                        if (SettingsClient.Get<bool>("Indexing", "RefreshCollectionAutomatically"))
                        {
                            this.AddArtworkInBackgroundAsync();
                            await this.watcherManager.StartWatchingAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogClient.Error("Could not check the collection. Exception: {0}", ex.Message);
            }
        }

        private async Task IndexCollectionAsync()
        {
            if (this.IsIndexing)
            {
                return;
            }

            this.isIndexing = true;

            this.IndexingStarted(this, new EventArgs());

            // Tracks
            // ------
            bool isTracksChanged = await this.IndexTracksAsync(
                SettingsClient.Get<bool>("Indexing", "IgnoreRemovedFiles")) > 0;

            // Track statistics (for upgrade from 1.x to 2.x)
            // ----------------------------------------------
            await this.MigrateTrackStatisticsIfExistsAsync();

            // Artwork cleanup
            // ---------------
            bool isArtworkCleanedUp = await this.CleanupArtworkAsync();

            // Refresh lists
            // -------------
            if (isTracksChanged || isArtworkCleanedUp)
            {
                LogClient.Info("Sending event to refresh the lists because: isTracksChanged = {0}, isArtworkCleanedUp = {1}", isTracksChanged, isArtworkCleanedUp);
                this.RefreshLists(this, new EventArgs());
            }

            // Finalize
            // --------
            this.isIndexing = false;
            this.IndexingStopped(this, new EventArgs());

            this.AddArtworkInBackgroundAsync();

            if (SettingsClient.Get<bool>("Indexing", "RefreshCollectionAutomatically"))
            {
                await this.watcherManager.StartWatchingAsync();
            }
        }

        private async Task MigrateTrackStatisticsIfExistsAsync()
        {
            await Task.Run(() =>
            {
                using (var conn = this.sqliteConnectionFactory.GetConnection())
                {
                    int count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='TrackStatistic'");

                    if (count > 0)
                    {
                        List<TrackStatistic> trackStatistics = conn.Query<TrackStatistic>("SELECT * FROM TrackStatistic WHERE (Rating IS NOT NULL AND Rating <> 0) " +
                            "OR (Love IS NOT NULL AND Love <> 0) " +
                            "OR (PlayCount IS NOT NULL AND PlayCount <> 0)" +
                            "OR (SkipCount IS NOT NULL AND SkipCount <> 0)" +
                            "OR (DateLastPlayed IS NOT NULL AND DateLastPlayed <> 0)");

                        foreach (TrackStatistic trackStatistic in trackStatistics)
                        {
                            conn.Execute("UPDATE Track SET Rating=?, Love=?, PlayCount=?, SkipCount=?, DateLastPlayed=? WHERE Safepath=?;",
                                trackStatistic.Rating, trackStatistic.Love, trackStatistic.PlayCount, trackStatistic.SkipCount, trackStatistic.DateLastPlayed, trackStatistic.SafePath);
                        }

                        conn.Execute("DROP TABLE TrackStatistic;");
                    }
                }
            });
        }

        private async Task<long> IndexTracksAsync(bool ignoreRemovedFiles)
        {
            LogClient.Info("+++ STARTED INDEXING COLLECTION +++");

            DateTime startTime = DateTime.Now;

            long numberTracksRemoved = 0;
            long numberTracksAdded = 0;
            long numberTracksUpdated = 0;

            try
            {
                // Step 1: remove Tracks which are not found on disk
                // -------------------------------------------------
                Stopwatch sw = Stopwatch.StartNew();

                numberTracksRemoved = await this.RemoveTracksAsync();
                LogClient.Info("Tracks removed: {0}. Time required: {1} ms +++", numberTracksRemoved, sw.ElapsedMilliseconds);

                sw = Stopwatch.StartNew();
                await this.GetNewDiskPathsAsync(ignoreRemovedFiles); // Obsolete tracks are removed, now we can determine new files.
                LogClient.Info("Got new disk paths: {0}. Time required: {1} ms +++", newDiskPaths.Count, sw.ElapsedMilliseconds);

                this.cache.Initialize(); // After obsolete tracks are removed, we can initialize the cache.

                // Step 2: update outdated Tracks
                // ------------------------------
                sw = Stopwatch.StartNew();
                numberTracksUpdated = await this.UpdateTracksAsync();

                LogClient.Info("Tracks updated: {0}. Time required: {1} ms +++", numberTracksUpdated, sw.ElapsedMilliseconds);

                // Step 3: add new Tracks
                // ----------------------
                sw = Stopwatch.StartNew();
                numberTracksAdded = await this.AddTracksAsync();

                LogClient.Info("Tracks added: {0}. Time required: {1} ms +++", numberTracksAdded, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                LogClient.Info("There was a problem while indexing the collection. Exception: {0}", ex.Message);
            }

            LogClient.Info("+++ FINISHED INDEXING COLLECTION: Tracks removed: {0}. Tracks updated: {1}. Tracks added: {2}. Time required: {3} ms +++", numberTracksRemoved, numberTracksUpdated, numberTracksAdded, Convert.ToInt64(DateTime.Now.Subtract(startTime).TotalMilliseconds));

            return numberTracksRemoved + numberTracksAdded + numberTracksUpdated;
        }

        class QuerySafePath
        {
            public string SafePath { get; set; }
        }

        private async Task GetNewDiskPathsAsync(bool ignoreRemovedFiles)
        {
            await Task.Run(() =>
            {
                HashSet<string> dbPaths;
                HashSet<string> removedPaths;

                using (var conn = this.sqliteConnectionFactory.GetConnection())
                {
                    dbPaths = conn.Query<QuerySafePath>("SELECT SafePath FROM Track").Select(s => s.SafePath).ToHashSet();
                    removedPaths = ignoreRemovedFiles
                        ? new HashSet<string>()
                        : conn.Query<QuerySafePath>("SELECT SafePath FROM RemovedTrack").Select(t => t.SafePath).ToHashSet();
                }

                this.newDiskPaths = new List<FolderPathInfo>();

                foreach (FolderPathInfo diskpath in this.allDiskPaths)
                {
                    if (!dbPaths.Contains(diskpath.Path.ToSafePath()) && (ignoreRemovedFiles || !removedPaths.Contains(diskpath.Path.ToSafePath())))
                    {
                        this.newDiskPaths.Add(diskpath);
                    }
                }
            });
        }

        class QueryResultTrackIdAndPath
        {
            public int TrackId { get; set; }
            public string Path { get; set; }
        }

        private async Task<long> RemoveTracksAsync()
        {
            long numberRemovedTracks = 0;

            var args = new IndexingStatusEventArgs()
            {
                IndexingAction = IndexingAction.RemoveTracks,
                ProgressPercent = 0
            };

            await Task.Run(() =>
            {
                try
                {
                    using (var conn = this.sqliteConnectionFactory.GetConnection())
                    {
                        LogClient.Info("Begin removing tracks");

                        // Delete all tracks with missing folders
                        {
                            int deletedTracks = conn.Execute("DELETE FROM Track WHERE TrackID NOT IN (SELECT TrackID FROM FolderTrack)");

                            if (deletedTracks > 0)
                            {
                                this.IndexingStatusChanged(args);
                                numberRemovedTracks += deletedTracks;
                            }
                        }

                        var existingTracks = conn.Query<QueryResultTrackIdAndPath>("SELECT TrackID, Path FROM Track").ToList();

                        Parallel.ForEach(
                            Partitioner.Create(0, existingTracks.Count, IndexerUtils.GetParallelBatchSize(existingTracks.Count)),
                            new ParallelOptions
                            {
                                CancellationToken = cancellationService.CancellationToken,
                                MaxDegreeOfParallelism = Environment.ProcessorCount,
                            },
                            range =>
                            {
                                LogClient.Info("Checking range {0}-{1}", range.Item1, range.Item2);

                                for (int i = range.Item1; i < range.Item2 && cancellationService.KeepRunning; ++i)
                                {
                                    var trackIdAndPath = existingTracks[i];

                                    if (!System.IO.File.Exists(trackIdAndPath.Path))
                                    {
                                        lock (conn)
                                        {
                                            conn.Delete<Track>(trackIdAndPath.TrackId);
                                        }
                                    }
                                }
                            });

                        LogClient.Info("Finished removing tracks");
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("There was a problem while removing Tracks. Exception: {0}", ex.Message);
                }
            });

            return numberRemovedTracks;
        }

        private async Task<long> UpdateTracksAsync()
        {
            long numberUpdatedTracks = 0;

            var args = new IndexingStatusEventArgs()
            {
                IndexingAction = IndexingAction.UpdateTracks,
                ProgressPercent = 0
            };

            await Task.Run(() =>
            {
                try
                {
                    using (var conn = this.sqliteConnectionFactory.GetConnection())
                    {
                        LogClient.Info("Starting updating tracks");

                        List<Track> alltracks = conn.Table<Track>().Select((t) => t).ToList();

                        long currentValue = 0;
                        long totalValue = alltracks.Count;
                        int lastPercent = 0;

                        int batchSize = IndexerUtils.GetParallelBatchSize(alltracks.Count);

                        Parallel.ForEach(
                            Partitioner.Create(0, alltracks.Count, batchSize),
                            new ParallelOptions
                            {
                                CancellationToken = cancellationService.CancellationToken,
                                MaxDegreeOfParallelism = Environment.ProcessorCount
                            },
                            range =>
                            {
                                for (int i = range.Item1; i < range.Item2 && cancellationService.KeepRunning; ++i)
                                {
                                    var dbTrack = alltracks[i];
                                    var tracksToUpdate = new List<Track>();

                                    try
                                    {
                                        if (IndexerUtils.IsTrackOutdated(dbTrack) || dbTrack.NeedsIndexing == 1)
                                        {
                                            this.ProcessTrack(dbTrack);
                                            tracksToUpdate.Add(dbTrack);
                                            conn.Update(dbTrack);
                                            numberUpdatedTracks += 1;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogClient.Error("There was a problem while updating Track with path='{0}'. Exception: {1}", dbTrack.Path, ex.Message);
                                    }

                                    lock (conn)
                                    {
                                        if (tracksToUpdate.Count > 0)
                                        {
                                            conn.BeginTransaction();
                                            conn.UpdateAll(tracksToUpdate);
                                            conn.Commit();
                                        }

                                        currentValue += (range.Item2 - range.Item1);

                                        int percent = IndexerUtils.CalculatePercent(currentValue, totalValue);

                                        lastPercent = percent;
                                        args.ProgressPercent = percent;
                                    }
                                }
                            });

                        LogClient.Info("Finished updating tracks");
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("There was a problem while updating Tracks. Exception: {0}", ex.Message);
                }
            });

            return numberUpdatedTracks;
        }

        private async Task<long> AddTracksAsync()
        {
            if(newDiskPaths.Count == 0)
            {
                return 0;
            }

            long numberAddedTracks = 0;

            var args = new IndexingStatusEventArgs()
            {
                IndexingAction = IndexingAction.AddTracks,
                ProgressPercent = 0,
                ProgressCurrent = 0
            };
            this.IndexingStatusChanged(args);

            await Task.Run(() =>
            {
                try
                {
                    long currentValue = 0;
                    long totalValue = this.newDiskPaths.Count;

                    int batchSize = IndexerUtils.GetParallelBatchSize(newDiskPaths.Count);

                    LogClient.Info("Processing {0} paths in batches of {1}", newDiskPaths.Count, batchSize);

                    using (var conn = this.sqliteConnectionFactory.GetConnection())
                    {
                        Parallel.ForEach(
                            Partitioner.Create(0, newDiskPaths.Count, batchSize),
                            new ParallelOptions
                            { 
                                CancellationToken = cancellationService.CancellationToken,
                                MaxDegreeOfParallelism = Environment.ProcessorCount
                            },
                            range =>
                        {
                            LogClient.Info("Processing range {0}-{1}", range.Item1, range.Item2);

                            var newDiskTracks = new List<Track>(range.Item2 - range.Item1);
                            var newFolderTracks = new List<Tuple<FolderPathInfo, Track>>(range.Item2 - range.Item1);

                            for (int i = range.Item1; i < range.Item2 && cancellationService.KeepRunning; ++i)
                            {
                                var diskPath = newDiskPaths[i];
                                var safePath = diskPath.Path.ToSafePath();

                                try
                                {
                                    var diskTrack = cache.GetTrack(safePath);

                                    if (diskTrack == null)
                                    {
                                        diskTrack = Track.CreateDefault(diskPath.Path);
                                        ProcessTrack(diskTrack);
                                        newDiskTracks.Add(diskTrack);
                                        Interlocked.Increment(ref numberAddedTracks);
                                    }

                                    newFolderTracks.Add(Tuple.Create(diskPath, diskTrack));
                                }
                                catch (Exception ex)
                                {
                                    LogClient.Error("There was a problem while adding Track with path='{0}'. Exception: {1}", diskPath, ex.Message);
                                }
                            }

                            LogClient.Info("Finished range {0}-{1}", range.Item1, range.Item2);

                            lock (conn)
                            {
                                conn.BeginTransaction();
                                conn.InsertAll(newDiskTracks);
                                conn.InsertAll(newFolderTracks.Select(nft => new FolderTrack(nft.Item1.FolderId, nft.Item2.TrackID)));
                                conn.Commit();

                                bool firstInsert = currentValue == 0;
                                currentValue += newFolderTracks.Count;

                                int percent = IndexerUtils.CalculatePercent(currentValue, totalValue);

                                args.ProgressCurrent = Interlocked.Read(ref numberAddedTracks);
                                args.ProgressPercent = percent;
                                this.IndexingStatusChanged(args);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    LogClient.Error("There was a problem while adding Tracks. Exception: {0}", ex.Message);
                }
            });

            return numberAddedTracks;
        }

        private void ProcessTrack(Track track)
        {
            try
            {
                MetadataUtils.FillTrack(new FileMetadata(track.Path), track);

                track.IndexingSuccess = 1;

                // Make sure that we check for album cover art
                track.NeedsAlbumArtworkIndexing = 1;
            }
            catch (Exception ex)
            {
                track.IndexingSuccess = 0;
                track.NeedsIndexing = 0; // Let's not keep trying to indexing this track
                track.IndexingFailureReason = ex.Message;

                LogClient.Error("Error while retrieving tag information for file {0}. Exception: {1}", track.Path, ex.Message);
            }
        }

        private async void WatcherManager_FoldersChanged(object sender, EventArgs e)
        {
            await this.RefreshCollectionAsync();
        }

        private async Task<long> DeleteUnusedArtworkFromCacheAsync()
        {
            long numberDeleted = 0;

            await Task.Run(async () =>
            {
                string[] artworkFiles = Directory.GetFiles(this.cacheService.CoverArtCacheFolderPath, "album-*.jpg");

                using (SQLiteConnection conn = this.sqliteConnectionFactory.GetConnection())
                {
                    IList<string> artworkIds = await this.albumArtworkRepository.GetArtworkIdsAsync();

                    foreach (string artworkFile in artworkFiles)
                    {
                        if (!artworkIds.Contains(Path.GetFileNameWithoutExtension(artworkFile)))
                        {
                            try
                            {
                                System.IO.File.Delete(artworkFile);
                                numberDeleted += 1;
                            }
                            catch (Exception ex)
                            {
                                LogClient.Error("There was a problem while deleting cached artwork {0}. Exception: {1}", artworkFile, ex.Message);
                            }
                        }
                    }
                }
            });

            return numberDeleted;
        }

        private async Task<bool> CleanupArtworkAsync()
        {
            LogClient.Info("+++ STARTED CLEANING UP ARTWORK +++");

            DateTime startTime = DateTime.Now;
            long numberDeletedFromDatabase = 0;
            long numberDeletedFromDisk = 0;

            try
            {
                // Step 1: delete unused AlbumArtwork from the database (Which isn't mapped to a Track's AlbumKey)
                // -----------------------------------------------------------------------------------------------
                numberDeletedFromDatabase = await this.albumArtworkRepository.DeleteUnusedAlbumArtworkAsync();

                // Step 2: delete unused artwork from the cache
                // --------------------------------------------
                numberDeletedFromDisk = await this.DeleteUnusedArtworkFromCacheAsync();
            }
            catch (Exception ex)
            {
                LogClient.Info("There was a problem while updating the artwork. Exception: {0}", ex.Message);
            }

            LogClient.Info("+++ FINISHED CLEANING UP ARTWORK: Covers deleted from database: {0}. Covers deleted from disk: {1}. Time required: {3} ms +++", numberDeletedFromDatabase, numberDeletedFromDisk, Convert.ToInt64(DateTime.Now.Subtract(startTime).TotalMilliseconds));

            return numberDeletedFromDatabase + numberDeletedFromDisk > 0;
        }

        private async Task<string> GetArtworkFromFile(string albumKey)
        {
            Track track = await this.trackRepository.GetLastModifiedTrackForAlbumKeyAsync(albumKey);
            return await this.cacheService.CacheArtworkAsync(IndexerUtils.GetArtwork(albumKey, new FileMetadata(track.Path)));
        }

        private async Task<string> GetArtworkFromInternet(string albumTitle, IList<string> albumArtists, string trackTitle, IList<string> artists)
        {
            string artworkUriString = await this.infoDownloadService.GetAlbumImageAsync(albumTitle, albumArtists, trackTitle, artists);
            return await this.cacheService.CacheArtworkAsync(artworkUriString);
        }

        private async void AddArtworkInBackgroundAsync()
        {
            // First, add artwork from file.
            await this.AddArtworkInBackgroundAsync(1);

            // Next, add artwork from the Internet, if the user has chosen to do so.
            if (SettingsClient.Get<bool>("Covers", "DownloadMissingAlbumCovers"))
            {
                // Add artwork from the Internet.
                await this.AddArtworkInBackgroundAsync(2);
            }

            // We don't need to scan for artwork anymore
            await this.trackRepository.DisableNeedsAlbumArtworkIndexingForAllTracksAsync();
        }

        private async Task AddArtworkInBackgroundAsync(int passNumber)
        {
            LogClient.Info("+++ STARTED ADDING ARTWORK IN THE BACKGROUND +++");
            this.canIndexArtwork = true;
            this.isIndexingArtwork = true;

            DateTime startTime = DateTime.Now;

            await Task.Run(async () =>
            {
                using (SQLiteConnection conn = this.sqliteConnectionFactory.GetConnection())
                {
                    try
                    {
                        IList<string> albumKeysWithArtwork = new List<string>();
                        IList<AlbumData> albumDatasToIndex = await this.trackRepository.GetAlbumDataToIndexAsync();

                        foreach (AlbumData albumDataToIndex in albumDatasToIndex)
                        {
                            // Check if we must cancel artwork indexing
                            if (!this.canIndexArtwork)
                            {
                                try
                                {
                                    LogClient.Info("+++ ABORTED ADDING ARTWORK IN THE BACKGROUND. Time required: {0} ms +++", Convert.ToInt64(DateTime.Now.Subtract(startTime).TotalMilliseconds));
                                    this.AlbumArtworkAdded(this, new AlbumArtworkAddedEventArgs() { AlbumKeys = albumKeysWithArtwork }); // Update UI
                                }
                                catch (Exception ex)
                                {
                                    LogClient.Error("Failed to commit changes while aborting adding artwork in background. Exception: {0}", ex.Message);
                                }

                                this.isIndexingArtwork = false;

                                return;
                            }

                            // Start indexing artwork
                            try
                            {
                                // Delete existing AlbumArtwork
                                await this.albumArtworkRepository.DeleteAlbumArtworkAsync(albumDataToIndex.AlbumKey);

                                // Create a new AlbumArtwork
                                var albumArtwork = new AlbumArtwork(albumDataToIndex.AlbumKey);

                                if (passNumber.Equals(1))
                                {
                                    // During the 1st pass, look for artwork in file(s).
                                    // Only set NeedsAlbumArtworkIndexing = 0 if artwork was found. So when no artwork was found, 
                                    // this gives the 2nd pass a chance to look for artwork on the Internet.
                                    albumArtwork.ArtworkID = await this.GetArtworkFromFile(albumDataToIndex.AlbumKey);

                                    if (!string.IsNullOrEmpty(albumArtwork.ArtworkID))
                                    {
                                        await this.trackRepository.DisableNeedsAlbumArtworkIndexingAsync(albumDataToIndex.AlbumKey);
                                    }
                                }
                                else if (passNumber.Equals(2))
                                {
                                    // During the 2nd pass, look for artwork on the Internet and set NeedsAlbumArtworkIndexing = 0.
                                    // We don't want future passes to index for this AlbumKey anymore.
                                    albumArtwork.ArtworkID = await this.GetArtworkFromInternet(
                                        albumDataToIndex.AlbumTitle,
                                        DataUtils.SplitAndTrimColumnMultiValue(albumDataToIndex.AlbumArtists).ToList(),
                                        albumDataToIndex.TrackTitle,
                                        DataUtils.SplitAndTrimColumnMultiValue(albumDataToIndex.Artists).ToList()
                                        );

                                    await this.trackRepository.DisableNeedsAlbumArtworkIndexingAsync(albumDataToIndex.AlbumKey);
                                }

                                // If artwork was found, keep track of the albumID
                                if (!string.IsNullOrEmpty(albumArtwork.ArtworkID))
                                {
                                    albumKeysWithArtwork.Add(albumArtwork.AlbumKey);
                                    conn.Insert(albumArtwork);
                                }

                                // If artwork was found for 20 albums, trigger a refresh of the UI.
                                if (albumKeysWithArtwork.Count >= 20)
                                {
                                    var eventAlbumKeys = new List<string>(albumKeysWithArtwork);
                                    albumKeysWithArtwork.Clear();
                                    this.AlbumArtworkAdded(this, new AlbumArtworkAddedEventArgs() { AlbumKeys = eventAlbumKeys }); // Update UI
                                }
                            }
                            catch (Exception ex)
                            {
                                LogClient.Error("There was a problem while updating the cover art for Album {0}/{1}. Exception: {2}", albumDataToIndex.AlbumTitle, albumDataToIndex.AlbumArtists, ex.Message);
                            }
                        }

                        try
                        {
                            this.AlbumArtworkAdded(this, new AlbumArtworkAddedEventArgs() { AlbumKeys = albumKeysWithArtwork }); // Update UI
                        }
                        catch (Exception ex)
                        {
                            LogClient.Error("Failed to commit changes while finishing adding artwork in background. Exception: {0}", ex.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogClient.Error("Unexpected error occurred while updating artwork in the background. Exception: {0}", ex.Message);
                    }
                }
            });

            this.isIndexingArtwork = false;
            LogClient.Error("+++ FINISHED ADDING ARTWORK IN THE BACKGROUND. Time required: {0} ms +++", Convert.ToInt64(DateTime.Now.Subtract(startTime).TotalMilliseconds));
        }

        public async void ReScanAlbumArtworkAsync(bool onlyWhenHasNoCover)
        {
            this.canIndexArtwork = false;

            // Wait until artwork indexing is stopped
            while (this.isIndexingArtwork)
            {
                await Task.Delay(100);
            }

            await this.trackRepository.EnableNeedsAlbumArtworkIndexingForAllTracksAsync(onlyWhenHasNoCover);

            this.AddArtworkInBackgroundAsync();
        }

        private async Task<List<FolderPathInfo>> GetFolderPaths()
        {
            var allFolderPaths = new List<FolderPathInfo>();
            List<Folder> folders = await this.folderRepository.GetFoldersAsync();

            // Recursively get all the files in the collection folders
            foreach (Folder fol in folders)
            {
                if (Directory.Exists(fol.Path))
                {
                    try
                    {
                        // Get all audio files recursively
                        List<FolderPathInfo> folderPaths = await FileOperations.GetValidFolderPathsAsync(
                            fol.FolderID,
                            fol.Path,
                            FileFormats.SupportedMediaExtensions,
                            cancellationService.CancellationToken);
                        allFolderPaths.AddRange(folderPaths);
                    }
                    catch (Exception ex)
                    {
                        LogClient.Error("Error while recursively getting files/folders for directory={0}. Exception: {1}", fol.Path, ex.Message);
                    }
                }
            }

            return allFolderPaths;
        }
    }
}
