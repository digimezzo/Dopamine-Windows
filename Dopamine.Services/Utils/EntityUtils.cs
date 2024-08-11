using Dopamine.Core.Base;
using Dopamine.Core.Utils;
using Dopamine.Data;
using Dopamine.Data.Entities;
using Dopamine.Data.Metadata;
using Dopamine.Services.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dopamine.Services.Utils
{
    public static class EntityUtils
    {
        public static bool FilterAlbums(AlbumViewModel album, string filter)
        {
            // Trim is required here, otherwise the filter might flip on the space at the beginning (and probably at the end)
            string[] pieces = filter.Trim().Split(Convert.ToChar(" "));

            return pieces.All((s) => 
            album.AlbumTitle.ToLower().Contains(s.ToLower()) | 
            album.AlbumArtist.ToLower().Contains(s.ToLower()) | 
            album.Year.ToString().ToLower().Contains(s.ToLower()));
        }

        public static bool FilterArtists(ArtistViewModel artist, string filter)
        {
            // Trim is required here, otherwise the filter might flip on the space at the beginning (and probably at the end)
            string[] pieces = filter.Trim().Split(Convert.ToChar(" "));

            return pieces.All((s) => artist.ArtistName.ToLower().Contains(s.ToLower()));
        }

        public static bool FilterGenres(GenreViewModel genre, string filter)
        {
            // Trim is required here, otherwise the filter might flip on the space at the beginning (and probably at the end)
            string[] pieces = filter.Trim().Split(Convert.ToChar(" "));

            return pieces.All((s) => genre.GenreName.ToLower().Contains(s.ToLower()));
        }

        public static bool FilterTracks(TrackViewModel track, string filter)
        {
            // Trim is required here, otherwise the filter might flip on the space at the beginning (and probably at the end)
            string[] pieces = filter.Trim().Split(Convert.ToChar(" "));

            return pieces.All((s) => 
            track.TrackTitle.ToLower().Contains(s.ToLower()) | 
            track.ArtistName.ToLower().Contains(s.ToLower()) | 
            track.AlbumTitle.ToLower().Contains(s.ToLower()) | 
            track.FileName.ToLower().Contains(s.ToLower()) | 
            track.Year.ToString().Contains(s.ToLower()));
        }

        public static async Task<List<TrackViewModel>> OrderTracksAsync(IList<TrackViewModel> tracks, TrackOrder trackOrder)
        {
            var orderedTracks = new List<TrackViewModel>();

            await Task.Run(() =>
            {
                switch (trackOrder)
                {
                    case TrackOrder.Alphabetical:
                        orderedTracks = tracks.OrderBy((t) => !string.IsNullOrEmpty(FormatUtils.GetSortableString(t.TrackTitle)) ? FormatUtils.GetSortableString(t.TrackTitle) : FormatUtils.GetSortableString(t.FileName)).ToList();
                        break;
                    case TrackOrder.ByAlbum:
                        orderedTracks = tracks.OrderBy((t) => FormatUtils.GetSortableString(t.AlbumArtist)).ThenBy((t) => FormatUtils.GetSortableString(t.AlbumTitle)).ThenBy((t) => t.SortDiscNumber).ThenBy((t) => t.SortTrackNumber).ToList();
                        break;
                    case TrackOrder.ByFileName:
                        orderedTracks = tracks.OrderBy((t) => FormatUtils.GetSortableString(t.FileName)).ToList();
                        break;
                    case TrackOrder.ByRating:
                        orderedTracks = tracks.OrderByDescending((t) => t.Rating).ToList();
                        break;
                    case TrackOrder.ByModification:
                        orderedTracks = tracks.OrderByDescending((t) => t.SortModificationDate).ToList();
                        break;
                    case TrackOrder.ReverseAlphabetical:
                        orderedTracks = tracks.OrderByDescending((t) => !string.IsNullOrEmpty(FormatUtils.GetSortableString(t.TrackTitle)) ? FormatUtils.GetSortableString(t.TrackTitle) : FormatUtils.GetSortableString(t.FileName)).ToList();
                        break;
                    case TrackOrder.None:
                        orderedTracks = tracks.ToList();
                        break;
                    default:
                        // By album
                        orderedTracks = tracks.OrderBy((t) => FormatUtils.GetSortableString(t.AlbumTitle)).ThenBy((t) => t.SortDiscNumber).ThenBy((t) => t.SortTrackNumber).ToList();
                        break;
                }
            });

            return orderedTracks;
        }
    }
}
