﻿using Dopamine.Common.Audio;
using Dopamine.Common.Base;
using Dopamine.Common.Database;
using Dopamine.Common.Database.Entities;
using Dopamine.Common.Helpers;
using Dopamine.Common.Metadata;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dopamine.Common.Services.Playback
{
    public delegate void PlaybackFailedEventHandler(object sender, PlaybackFailedEventArgs e);

    public interface IPlaybackService
    {
        #region ReadOnly Properties
        IPlayer Player { get; }
        KeyValuePair<string, PlayableTrack> CurrentTrack { get; }
        bool HasCurrentTrack { get; }
        bool IsSavingQueuedTracks { get; }
        bool IsSavingPlaybackCounters { get; }
        OrderedDictionary<string, PlayableTrack> Queue { get; }
        bool Shuffle { get; }
        bool Mute { get; }
        bool IsStopped { get; }
        bool IsPlaying { get; }
        TimeSpan GetCurrentTime { get; }
        TimeSpan GetTotalTime { get; }
        #endregion

        #region Properties
        double Progress { get; set; }
        float Volume { get; set; }
        LoopMode LoopMode { get; set; }
        int Latency { get; set; }
        bool EventMode { get; set; }
        bool ExclusiveMode { get; set; }
        bool IsSpectrumVisible { get; set; }
        #endregion

        #region Functions
        void Stop();
        void Skip(double progress);
        void SetMute(bool mute);
        Task SetShuffleAsync(bool shuffle);
        Task PlayNextAsync();
        Task PlayPreviousAsync();
        Task PlayOrPauseAsync();
        Task PlaySelectedAsync(PlayableTrack track);
        Task PlaySelectedAsync(KeyValuePair<string, PlayableTrack> trackPair);
        Task EnqueueAsync();
        Task EnqueueAsync(List<KeyValuePair<string, PlayableTrack>> trackPairs, KeyValuePair<string, PlayableTrack> track);
        Task EnqueueAsync(List<KeyValuePair<string, PlayableTrack>> trackPairs);
        Task EnqueueAsync(List<PlayableTrack> tracks, PlayableTrack track);
        Task EnqueueAsync(List<PlayableTrack> tracks);
        Task EnqueueAsync(Artist artist);
        Task EnqueueAsync(Genre genre);
        Task EnqueueAsync(Album album);
        Task ShuffleAllAsync();
        Task StopIfPlayingAsync(PlayableTrack track);
        Task<EnqueueResult> AddToQueueAsync(IList<PlayableTrack> tracks);
        Task<EnqueueResult> AddToQueueAsync(IList<Artist> artists);
        Task<EnqueueResult> AddToQueueAsync(IList<Genre> genres);
        Task<EnqueueResult> AddToQueueAsync(IList<Album> albums);
        Task<EnqueueResult> AddToQueueNextAsync(IList<PlayableTrack> tracks);
        Task<DequeueResult> DequeueAsync(IList<PlayableTrack> tracks);
        Task<DequeueResult> DequeueAsync(IList<KeyValuePair<string, PlayableTrack>> tracks);
        Task SaveQueuedTracksAsync();
        Task SavePlaybackCountersAsync();
        void ApplyPreset(EqualizerPreset preset);
        Task SetIsEqualizerEnabledAsync(bool isEnabled);
        Task UpdateQueueMetadataAsync(List<FileMetadata> fileMetadatas);
        Task UpdateQueueOrderAsync(List<KeyValuePair<string, PlayableTrack>> tracks);
        #endregion

        #region Events
        event Action<bool> PlaybackSuccess;
        event PlaybackFailedEventHandler PlaybackFailed;
        event EventHandler PlaybackStopped;
        event EventHandler PlaybackPaused;
        event EventHandler PlaybackResumed;
        event EventHandler PlaybackProgressChanged;
        event EventHandler PlaybackVolumeChanged;
        event EventHandler PlaybackMuteChanged;
        event EventHandler PlaybackLoopChanged;
        event EventHandler PlaybackShuffleChanged;
        event Action<bool> SpectrumVisibilityChanged;
        event Action<int> AddedTracksToQueue;
        event EventHandler PlaybackCountersChanged;
        event Action<bool> LoadingTrack;
        event EventHandler PlayingTrackPlaybackInfoChanged;
        event EventHandler PlayingTrackArtworkChanged;
        event EventHandler QueueChanged;
        #endregion
    }
}
