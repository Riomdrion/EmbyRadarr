using System;

namespace EmbyRadarr.Radarr
{
    /// <summary>A Radarr failure with a message that is safe to show in the Emby UI (never contains the API key).</summary>
    public class RadarrException : Exception
    {
        public RadarrException(string message, Exception inner = null)
            : base(message, inner)
        {
        }
    }
}
