using System.Runtime.Serialization;

namespace EmbyRadarr.Radarr
{
    // Only the fields this plugin uses. Names match Radarr's OpenAPI spec (src/Radarr.Api.V3/openapi.json).
    // Unknown JSON members are ignored by DataContractJsonSerializer.

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
}
