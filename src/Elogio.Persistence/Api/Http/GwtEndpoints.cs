namespace Elogio.Persistence.Api.Http;

/// <summary>
/// Constants for GWT endpoint paths.
/// Consolidates URLs that were previously hardcoded across KelioClient methods.
/// </summary>
public static class GwtEndpoints
{
    // BWP Servlet
    public const string BwpServlet = "/open/bwpDispatchServlet";

    // Portal pages
    public const string PortalJsp = "/open/bwt/portail.jsp";
    public const string CalendarAbsenceJsp = "/open/bwt/intranet_calendrier_absence.jsp";

    // Portal GWT files
    public const string PortalNoCacheJs = "/open/bwt/portail/portail.nocache.js";
    public const string PortalCacheJs = "/open/bwt/portail/85D2B992F6111BC9BF615C4D657B05CC.cache.js";

    // Declaration app GWT files
    public const string DeclarationNoCacheJs = "/open/bwt/app_declaration_desktop/app_declaration_desktop.nocache.js";
    public const string DeclarationCacheJs = "/open/bwt/app_declaration_desktop/1A313ED29AA1E74DD777D2CCF3248188.cache.js";

    // Calendar absence GWT files
    public const string CalendarAbsenceNoCacheJs = "/open/bwt/intranet_calendrier_absence/intranet_calendrier_absence.nocache.js";
    public const string CalendarAbsenceCacheJs = "/open/bwt/intranet_calendrier_absence/B774D9023F6AE5125A0446A2F6C1BC19.cache.js";

    // App launcher
    public const string AppLauncherDeclaration = "/open/bwt/appLauncher.jsp?app=app_declaration_desktop&appParams=idMenuDeclaration=1";

    // Push endpoint
    public const string PushConnect = "/open/push/connect";
}
