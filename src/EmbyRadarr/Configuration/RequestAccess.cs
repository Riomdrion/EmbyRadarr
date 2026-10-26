using System.ComponentModel;

namespace EmbyRadarr.Configuration
{
    public enum RequestAccess
    {
        [Description("Everyone with an Emby account")]
        Everyone,

        [Description("Administrators only")]
        AdministratorsOnly,
    }
}
