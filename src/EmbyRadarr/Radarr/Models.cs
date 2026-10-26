using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EmbyRadarr.Radarr
{
    // Only the fields this plugin uses. Names match Radarr's OpenAPI spec (src/Radarr.Api.V3/openapi.json).
    // Unknown JSON members are ignored by DataContractJsonSerializer.
    // Dates are kept as strings: DataContractJsonSerializer can't parse ISO 8601 into DateTime.

    [DataContract]
    public class SystemStatus
    {
        [DataMember(Name = "appName")]
        public string AppName { get; set; }

        [DataMember(Name = "version")]
        public string Version { get; set; }
    }

    [DataContract]
    public class QualityProfile
    {
        [DataMember(Name = "id")]
        public int Id { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }
    }

    [DataContract]
    public class RootFolder
    {
        [DataMember(Name = "id")]
        public int Id { get; set; }

        [DataMember(Name = "path")]
        public string Path { get; set; }

        [DataMember(Name = "accessible")]
        public bool Accessible { get; set; }
    }

    [DataContract]
    public class Movie
    {
        [DataMember(Name = "id")]
        public int Id { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }

        [DataMember(Name = "year")]
        public int Year { get; set; }

        [DataMember(Name = "overview")]
        public string Overview { get; set; }

        [DataMember(Name = "monitored")]
        public bool Monitored { get; set; }

        [DataMember(Name = "hasFile")]
        public bool HasFile { get; set; }

        [DataMember(Name = "isAvailable")]
        public bool IsAvailable { get; set; }

        [DataMember(Name = "inCinemas")]
        public string InCinemas { get; set; }

        [DataMember(Name = "digitalRelease")]
        public string DigitalRelease { get; set; }

        [DataMember(Name = "physicalRelease")]
        public string PhysicalRelease { get; set; }

        [DataMember(Name = "added")]
        public string Added { get; set; }

        [DataMember(Name = "runtime")]
        public int Runtime { get; set; }

        [DataMember(Name = "certification")]
        public string Certification { get; set; }

        [DataMember(Name = "genres")]
        public List<string> Genres { get; set; }

        [DataMember(Name = "studio")]
        public string Studio { get; set; }

        [DataMember(Name = "imdbId")]
        public string ImdbId { get; set; }

        [DataMember(Name = "tmdbId")]
        public int TmdbId { get; set; }

        [DataMember(Name = "youTubeTrailerId")]
        public string YouTubeTrailerId { get; set; }

        [DataMember(Name = "remotePoster")]
        public string RemotePoster { get; set; }

        [DataMember(Name = "images")]
        public List<MediaCover> Images { get; set; }

        // Only set by the Discover list (/api/v3/importlist/movie).
        [DataMember(Name = "isExisting")]
        public bool IsExisting { get; set; }

        [DataMember(Name = "isExcluded")]
        public bool IsExcluded { get; set; }

        [DataMember(Name = "isTrending")]
        public bool IsTrending { get; set; }

        [DataMember(Name = "isPopular")]
        public bool IsPopular { get; set; }

        [DataMember(Name = "isRecommendation")]
        public bool IsRecommendation { get; set; }
    }

    /// <summary>Body for POST /api/v3/movie.</summary>
    [DataContract]
    public class AddMovieRequest
    {
        [DataMember(Name = "title")]
        public string Title { get; set; }

        [DataMember(Name = "tmdbId")]
        public int TmdbId { get; set; }

        [DataMember(Name = "year")]
        public int Year { get; set; }

        [DataMember(Name = "qualityProfileId")]
        public int QualityProfileId { get; set; }

        [DataMember(Name = "rootFolderPath")]
        public string RootFolderPath { get; set; }

        [DataMember(Name = "monitored")]
        public bool Monitored { get; set; } = true;

        [DataMember(Name = "minimumAvailability")]
        public string MinimumAvailability { get; set; } = "released";

        [DataMember(Name = "images", EmitDefaultValue = false)]
        public List<MediaCover> Images { get; set; }

        [DataMember(Name = "addOptions")]
        public AddOptions AddOptions { get; set; } = new AddOptions();
    }

    [DataContract]
    public class AddOptions
    {
        [DataMember(Name = "searchForMovie")]
        public bool SearchForMovie { get; set; } = true;
    }

    /// <summary>Body for POST /api/v3/command, e.g. MoviesSearch or MissingMoviesSearch.</summary>
    [DataContract]
    public class CommandRequest
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "movieIds", EmitDefaultValue = false)]
        public List<int> MovieIds { get; set; }
    }

    [DataContract]
    public class CommandResult
    {
        [DataMember(Name = "id")]
        public int Id { get; set; }

        [DataMember(Name = "status")]
        public string Status { get; set; }
    }

    [DataContract]
    public class MediaCover
    {
        [DataMember(Name = "coverType")]
        public string CoverType { get; set; }

        [DataMember(Name = "url", EmitDefaultValue = false)]
        public string Url { get; set; }

        [DataMember(Name = "remoteUrl")]
        public string RemoteUrl { get; set; }
    }

    [DataContract]
    public class QueuePage
    {
        [DataMember(Name = "totalRecords")]
        public int TotalRecords { get; set; }

        [DataMember(Name = "records")]
        public List<QueueItem> Records { get; set; }
    }

    [DataContract]
    public class QueueItem
    {
        [DataMember(Name = "id")]
        public int Id { get; set; }

        [DataMember(Name = "movieId")]
        public int MovieId { get; set; }

        [DataMember(Name = "movie")]
        public Movie Movie { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }

        [DataMember(Name = "size")]
        public double Size { get; set; }

        [DataMember(Name = "sizeleft")]
        public double SizeLeft { get; set; }

        /// <summary>queued, paused, downloading, completed, failed, warning, delay, ...</summary>
        [DataMember(Name = "status")]
        public string Status { get; set; }

        /// <summary>downloading, importBlocked, importPending, importing, imported, failedPending, failed, ignored</summary>
        [DataMember(Name = "trackedDownloadState")]
        public string TrackedDownloadState { get; set; }

        /// <summary>A .NET TimeSpan string, e.g. "00:12:34" or "1.02:03:04".</summary>
        [DataMember(Name = "timeleft")]
        public string TimeLeft { get; set; }

        [DataMember(Name = "errorMessage")]
        public string ErrorMessage { get; set; }
    }
}
