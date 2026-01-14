using System.Text;

namespace Elogio.Persistence.Protocol;

/// <summary>
/// Builder for GWT-RPC request messages.
/// Based on reverse-engineered Kelio API protocol.
/// </summary>
public class GwtRpcRequestBuilder
{
    private const string BwpRequestType = "com.bodet.bwt.core.type.communication.BWPRequest";
    private const string JavaUtilList = "java.util.List";
    private const string JavaLangString = "java.lang.String";
    private const string JavaLangInteger = "java.lang.Integer";
    private const string BDateType = "com.bodet.bwt.core.type.time.BDate";
    private const string NullString = "NULL";

    /// <summary>
    /// Build a GWT-RPC request for the getSemaine method.
    /// </summary>
    /// <param name="sessionId">The session ID from authentication</param>
    /// <param name="date">The date for the week (YYYYMMDD format integer)</param>
    /// <param name="employeeId">The employee ID (extracted from GlobalBWTService connect)</param>
    public string BuildGetSemaineRequest(string sessionId, int date, int employeeId)
    {
        const string service = "com.bodet.bwt.app.portail.serveur.service.declaration.presence.DeclarationPresenceCompteurBWTService";
        const string method = "getSemaine";

        // String table (0-indexed in the format)
        var strings = new List<string>
        {
            BwpRequestType,     // 0
            JavaUtilList,       // 1
            BDateType,          // 2
            NullString,         // 3
            JavaLangInteger,    // 4
            JavaLangString,     // 5
            sessionId,          // 6
            method,             // 7
            service             // 8
        };

        var sb = new StringBuilder();

        // String table count
        sb.Append(strings.Count);

        // Add quoted strings
        foreach (var str in strings)
        {
            sb.Append(",\"");
            sb.Append(EscapeString(str));
            sb.Append('"');
        }

        // Add data tokens
        // Format derived from captured requests:
        // 0,1,2,2,{date},3,4,{employeeId},5,6,5,7,5,8
        sb.Append(",0,1,2,2,");
        sb.Append(date);
        sb.Append(",3,4,");
        sb.Append(employeeId);
        sb.Append(",5,6,5,7,5,8");

        return sb.ToString();
    }

    /// <summary>
    /// Build a GWT-RPC request for the getHeureServeur method.
    /// </summary>
    /// <param name="sessionId">The session ID from authentication</param>
    public string BuildGetServerTimeRequest(string sessionId)
    {
        const string service = "com.bodet.bwt.global.serveur.service.GlobalBWTService";
        const string method = "getHeureServeur";

        var strings = new List<string>
        {
            BwpRequestType,     // 0
            JavaUtilList,       // 1
            JavaLangInteger,    // 2
            JavaLangString,     // 3
            sessionId,          // 4
            method,             // 5
            service             // 6
        };

        var sb = new StringBuilder();
        sb.Append(strings.Count);

        foreach (var str in strings)
        {
            sb.Append(",\"");
            sb.Append(EscapeString(str));
            sb.Append('"');
        }

        // Data tokens for getHeureServeur
        // Format: 0,1,0,2,226,3,4,3,5,3,6
        sb.Append(",0,1,0,2,226,3,4,3,5,3,6");

        return sb.ToString();
    }

    /// <summary>
    /// Build a GWT-RPC request for the getTraductions method.
    /// Browser calls this multiple times with different prefixes before getSemaine.
    /// </summary>
    /// <param name="sessionId">The session ID from authentication</param>
    /// <param name="prefix">Translation prefix (e.g., "global_", "app.portail.declaration_")</param>
    /// <param name="employeeId">The employee ID (extracted from GlobalBWTService connect)</param>
    public string BuildGetTraductionsRequest(string sessionId, string prefix, int employeeId)
    {
        const string service = "com.bodet.bwt.global.serveur.service.GlobalBWTService";
        const string method = "getTraductions";

        // String table based on browser capture:
        // 8,"BWPRequest","java.util.List","java.lang.String","global_","java.lang.Integer","sessionId","getTraductions","GlobalBWTService"
        var strings = new List<string>
        {
            BwpRequestType,     // 0
            JavaUtilList,       // 1
            JavaLangString,     // 2
            prefix,             // 3
            JavaLangInteger,    // 4
            sessionId,          // 5
            method,             // 6
            service             // 7
        };

        var sb = new StringBuilder();
        sb.Append(strings.Count);

        foreach (var str in strings)
        {
            sb.Append(",\"");
            sb.Append(EscapeString(str));
            sb.Append('"');
        }

        // Data tokens from browser: 0,1,1,2,3,4,{employeeId},2,5,2,6,2,7
        sb.Append(",0,1,1,2,3,4,");
        sb.Append(employeeId);
        sb.Append(",2,5,2,6,2,7");

        return sb.ToString();
    }

    /// <summary>
    /// Build a GWT-RPC request for the PortailBWTService connect method (initial login).
    /// </summary>
    /// <param name="sessionId">The session ID from authentication</param>
    /// <param name="timestamp">Unix timestamp</param>
    public string BuildConnectRequest(string sessionId, long timestamp)
    {
        const string service = "com.bodet.bwt.portail.serveur.service.exec.PortailBWTService";
        const string method = "connect";
        const string targetType = "com.bodet.bwt.portail.serveur.domain.commun.TargetBWT";
        const string javaLangLong = "java.lang.Long";
        const string javaLangBoolean = "java.lang.Boolean";

        var strings = new List<string>
        {
            BwpRequestType,     // 0
            JavaUtilList,       // 1
            javaLangLong,       // 2
            targetType,         // 3
            javaLangBoolean,    // 4
            NullString,         // 5
            JavaLangString,     // 6
            sessionId,          // 7
            method,             // 8
            service             // 9
        };

        var sb = new StringBuilder();
        sb.Append(strings.Count);

        foreach (var str in strings)
        {
            sb.Append(",\"");
            sb.Append(EscapeString(str));
            sb.Append('"');
        }

        // Data tokens for connect
        // Format: 0,1,2,2,411,-timestamp,3,4,0,4,0,4,1,5,6,7,6,8,6,9
        sb.Append(",0,1,2,2,411,");
        sb.Append(-timestamp);
        sb.Append(",3,4,0,4,0,4,1,5,6,7,6,8,6,9");

        return sb.ToString();
    }

    /// <summary>
    /// Build a GWT-RPC request for the GlobalBWTService connect method.
    /// This returns the employee ID that must be used for subsequent API calls.
    /// </summary>
    /// <param name="sessionId">The session ID from authentication</param>
    /// <param name="timestamp">Unix timestamp</param>
    public string BuildGlobalConnectRequest(string sessionId, long timestamp)
    {
        const string service = "com.bodet.bwt.global.serveur.service.GlobalBWTService";
        const string method = "connect";
        const string javaLangShort = "java.lang.Short";
        const string javaLangLong = "java.lang.Long";

        var strings = new List<string>
        {
            BwpRequestType,     // 0
            JavaUtilList,       // 1
            javaLangShort,      // 2
            javaLangLong,       // 3
            NullString,         // 4
            JavaLangString,     // 5
            sessionId,          // 6
            method,             // 7
            service             // 8
        };

        var sb = new StringBuilder();
        sb.Append(strings.Count);

        foreach (var str in strings)
        {
            sb.Append(",\"");
            sb.Append(EscapeString(str));
            sb.Append('"');
        }

        // Data tokens for GlobalBWTService connect
        // CRITICAL: Browser sends Short=21, Long=411, Long=-timestamp
        // Format: 0,1,2,2,21,3,411,-timestamp,4,5,6,5,7,5,8
        sb.Append(",0,1,2,2,21,3,411,");
        sb.Append(-timestamp);
        sb.Append(",4,5,6,5,7,5,8");

        return sb.ToString();
    }

    /// <summary>
    /// Build a GWT-RPC request for the GlobalBWTService connect method for the calendar app.
    /// Uses Short=16 instead of 21 (portal uses 21, calendar uses 16 based on HAR capture).
    /// </summary>
    /// <param name="sessionId">The session ID from authentication</param>
    /// <param name="timestamp">Unix timestamp</param>
    public string BuildCalendarConnectRequest(string sessionId, long timestamp)
    {
        const string service = "com.bodet.bwt.global.serveur.service.GlobalBWTService";
        const string method = "connect";
        const string javaLangShort = "java.lang.Short";
        const string javaLangLong = "java.lang.Long";

        var strings = new List<string>
        {
            BwpRequestType,     // 0
            JavaUtilList,       // 1
            javaLangShort,      // 2
            javaLangLong,       // 3
            NullString,         // 4
            JavaLangString,     // 5
            sessionId,          // 6
            method,             // 7
            service             // 8
        };

        var sb = new StringBuilder();
        sb.Append(strings.Count);

        foreach (var str in strings)
        {
            sb.Append(",\"");
            sb.Append(EscapeString(str));
            sb.Append('"');
        }

        // Data tokens for calendar GlobalBWTService connect
        // CRITICAL: Calendar app uses Short=16 (vs 21 for portal), Long=411, Long=-timestamp
        // Format: 0,1,2,2,16,3,411,-timestamp,4,5,6,5,7,5,8
        sb.Append(",0,1,2,2,16,3,411,");
        sb.Append(-timestamp);
        sb.Append(",4,5,6,5,7,5,8");

        return sb.ToString();
    }

    /// <summary>
    /// Convert a DateOnly to Kelio date format (YYYYMMDD).
    /// </summary>
    public static int ToKelioDate(DateOnly date)
    {
        return date.Year * 10000 + date.Month * 100 + date.Day;
    }

    /// <summary>
    /// Convert a DateTime to Kelio date format (YYYYMMDD).
    /// </summary>
    public static int ToKelioDate(DateTime date)
    {
        return date.Year * 10000 + date.Month * 100 + date.Day;
    }

    /// <summary>
    /// Build a GWT-RPC request for the badgerSignaler method (clock-in/clock-out).
    /// This triggers a punch operation - the server determines whether it's clock-in or clock-out
    /// based on the employee's current state.
    /// </summary>
    /// <param name="sessionId">The session ID from authentication</param>
    /// <param name="employeeId">The employee ID</param>
    /// <remarks>
    /// Service: com.bodet.bwt.portail.serveur.service.commun.vignette.presence.BadgerSignalerPortailBWTService
    /// Method: badgerSignaler
    ///
    /// Request format (from HAR capture):
    /// 9,"BWPRequest","List","NULL","Boolean","Integer","String","{sessionId}","badgerSignaler","{service}",
    /// 0,1,3,2,2,3,0,4,{employeeId},5,6,5,7,5,8
    /// </remarks>
    public string BuildBadgerSignalerRequest(string sessionId, int employeeId)
    {
        const string service = "com.bodet.bwt.portail.serveur.service.commun.vignette.presence.BadgerSignalerPortailBWTService";
        const string method = "badgerSignaler";

        // String table based on captured HAR data
        var strings = new List<string>
        {
            BwpRequestType,     // 0
            JavaUtilList,       // 1
            NullString,         // 2
            "java.lang.Boolean",// 3
            JavaLangInteger,    // 4
            JavaLangString,     // 5
            sessionId,          // 6
            method,             // 7
            service             // 8
        };

        var sb = new StringBuilder();
        sb.Append(strings.Count);

        foreach (var str in strings)
        {
            sb.Append(",\"");
            sb.Append(EscapeString(str));
            sb.Append('"');
        }

        // Data tokens from HAR capture analysis:
        // 0,1 = BWPRequest, List (base types)
        // 3,2,2,3,0 = Boolean(NULL, NULL, Boolean=false) - parameters before employeeId
        // 4,{employeeId} = Integer with employee ID value
        // 5,6,5,7,5,8 = String references for sessionId, method, service
        sb.Append(",0,1,3,2,2,3,0,4,");
        sb.Append(employeeId);
        sb.Append(",5,6,5,7,5,8");

        return sb.ToString();
    }

    /// <summary>
    /// Build a GWT-RPC request for the GlobalBWTService getPresentationModel method
    /// with the GlobalPresentationModel class (called before CalendrierAbsencePresentationModel).
    /// </summary>
    /// <param name="sessionId">The session ID from authentication</param>
    /// <param name="contextId">Context ID (employee ID from session)</param>
    public string BuildGetGlobalPresentationModelRequest(string sessionId, int contextId)
    {
        const string service = "com.bodet.bwt.global.serveur.service.GlobalBWTService";
        const string method = "getPresentationModel";
        const string presentationModelClass = "com.bodet.bwt.appli.mouse.pm.global.GlobalPresentationModel";
        const string javaLangShort = "java.lang.Short";

        var strings = new List<string>
        {
            BwpRequestType,          // 0
            JavaUtilList,            // 1
            javaLangShort,           // 2
            JavaLangString,          // 3
            presentationModelClass,  // 4
            JavaLangInteger,         // 5
            sessionId,               // 6
            method,                  // 7
            service                  // 8
        };

        var sb = new StringBuilder();
        sb.Append(strings.Count);

        foreach (var str in strings)
        {
            sb.Append(",\"");
            sb.Append(EscapeString(str));
            sb.Append('"');
        }

        // Data tokens: 0,1,2,2,16,3,4,5,{contextId},3,6,3,7,3,8
        sb.Append(",0,1,2,2,16,3,4,5,");
        sb.Append(contextId);
        sb.Append(",3,6,3,7,3,8");

        return sb.ToString();
    }

    /// <summary>
    /// Build a GWT-RPC request for the LiensBWTService getParametreIntranet method.
    /// This is called during calendar initialization (after GlobalPresentationModel, before translations).
    /// </summary>
    /// <param name="sessionId">The session ID from authentication</param>
    /// <param name="contextId">Context ID (employee ID from session)</param>
    public string BuildGetParametreIntranetRequest(string sessionId, int contextId)
    {
        const string service = "com.bodet.bwt.commun.serveur.service.LiensBWTService";
        const string method = "getParametreIntranet";

        // HAR: 7,"BWPRequest","List","Integer","String","{sessionId}","getParametreIntranet","{service}"
        // Data: 0,1,0,2,{contextId},3,4,3,5,3,6
        var strings = new List<string>
        {
            BwpRequestType,     // 0
            JavaUtilList,       // 1
            JavaLangInteger,    // 2
            JavaLangString,     // 3
            sessionId,          // 4
            method,             // 5
            service             // 6
        };

        var sb = new StringBuilder();
        sb.Append(strings.Count);

        foreach (var str in strings)
        {
            sb.Append(",\"");
            sb.Append(EscapeString(str));
            sb.Append('"');
        }

        // Data tokens: 0,1,0,2,{contextId},3,4,3,5,3,6
        sb.Append(",0,1,0,2,");
        sb.Append(contextId);
        sb.Append(",3,4,3,5,3,6");

        return sb.ToString();
    }

    /// <summary>
    /// Build a GWT-RPC request for the GlobalBWTService getPresentationModel method
    /// with the CalendrierAbsencePresentationModel class.
    /// This MUST be called before getAbsencesEtJoursFeries to initialize the calendar module.
    /// </summary>
    /// <param name="sessionId">The session ID from authentication</param>
    /// <param name="contextId">Context ID (employee ID from session)</param>
    /// <remarks>
    /// Service: com.bodet.bwt.global.serveur.service.GlobalBWTService
    /// Method: getPresentationModel
    ///
    /// Request format (from HAR capture):
    /// 9,"BWPRequest","List","Short","String","CalendrierAbsencePresentationModel","Integer",
    /// "{sessionId}","getPresentationModel","{service}",
    /// 0,1,2,2,16,3,4,5,{contextId},3,6,3,7,3,8
    /// </remarks>
    public string BuildGetPresentationModelRequest(string sessionId, int contextId)
    {
        const string service = "com.bodet.bwt.global.serveur.service.GlobalBWTService";
        const string method = "getPresentationModel";
        const string presentationModelClass = "com.bodet.bwt.exploit.gtp.client.commun.intranet_calendrier_absence.CalendrierAbsencePresentationModel";
        const string javaLangShort = "java.lang.Short";

        // String table based on captured HAR data
        var strings = new List<string>
        {
            BwpRequestType,          // 0
            JavaUtilList,            // 1
            javaLangShort,           // 2
            JavaLangString,          // 3
            presentationModelClass,  // 4
            JavaLangInteger,         // 5
            sessionId,               // 6
            method,                  // 7
            service                  // 8
        };

        var sb = new StringBuilder();
        sb.Append(strings.Count);

        foreach (var str in strings)
        {
            sb.Append(",\"");
            sb.Append(EscapeString(str));
            sb.Append('"');
        }

        // Data tokens from HAR capture:
        // 0,1 = BWPRequest, List
        // 2,2,16 = Short type, Short value 16 (calendar module)
        // 3,4 = String type, presentation model class name
        // 5,{contextId} = Integer type, context ID value
        // 3,6,3,7,3,8 = String refs for sessionId, method, service
        sb.Append(",0,1,2,2,16,3,4,5,");
        sb.Append(contextId);
        sb.Append(",3,6,3,7,3,8");

        return sb.ToString();
    }

    /// <summary>
    /// Build a GWT-RPC request for the getAbsencesEtJoursFeries method.
    /// Retrieves absence calendar data (vacation, sick leave, holidays, etc.) for a date range.
    /// </summary>
    /// <param name="sessionId">The session ID from authentication</param>
    /// <param name="employeeId">The employee ID</param>
    /// <param name="startDate">Start date in YYYYMMDD format</param>
    /// <param name="endDate">End date in YYYYMMDD format</param>
    /// <param name="requestId">Request counter (used for internal tracking)</param>
    /// <remarks>
    /// Service: com.bodet.bwt.gtp.serveur.service.intranet.calendrier_absence.CalendrierAbsenceSalarieBWTService
    /// Method: getAbsencesEtJoursFeries
    ///
    /// Request format (from HAR capture):
    /// 11,"BWPRequest","List","Integer","BDate","CalendrierAbsenceConfigurationBWT","Boolean","NULL","String",
    /// "{sessionId}","getAbsencesEtJoursFeries","{service}",
    /// 0,1,5,2,{employeeId},3,{startDate},3,{endDate},4,5,1,5,0,5,1,5,1,5,1,5,1,6,6,5,1,6,6,2,3,2,{requestId},7,8,7,9,7,10
    /// </remarks>
    public string BuildGetAbsencesRequest(string sessionId, int employeeId, int startDate, int endDate, int requestId)
    {
        const string service = "com.bodet.bwt.gtp.serveur.service.intranet.calendrier_absence.CalendrierAbsenceSalarieBWTService";
        const string method = "getAbsencesEtJoursFeries";
        const string configType = "com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierAbsenceConfigurationBWT";
        const string javaLangBoolean = "java.lang.Boolean";

        // String table based on captured HAR data
        var strings = new List<string>
        {
            BwpRequestType,     // 0
            JavaUtilList,       // 1
            JavaLangInteger,    // 2
            BDateType,          // 3
            configType,         // 4
            javaLangBoolean,    // 5
            NullString,         // 6
            JavaLangString,     // 7
            sessionId,          // 8
            method,             // 9
            service             // 10
        };

        var sb = new StringBuilder();
        sb.Append(strings.Count);

        foreach (var str in strings)
        {
            sb.Append(",\"");
            sb.Append(EscapeString(str));
            sb.Append('"');
        }

        // Data tokens from HAR capture:
        // 0,1,5 = BWPRequest, List, with 5 parameters
        // 2,{employeeId} = Integer with employee ID
        // 3,{startDate},3,{endDate} = BDate start and end
        // 4,5,1,5,0,5,1,5,1,5,1,5,1,6,6,5,1,6,6 = CalendrierAbsenceConfigurationBWT with boolean flags
        // 2,3 = type refs
        // 2,{requestId} = Integer request counter
        // 7,8,7,9,7,10 = String references for sessionId, method, service
        sb.Append(",0,1,5,2,");
        sb.Append(employeeId);
        sb.Append(",3,");
        sb.Append(startDate);
        sb.Append(",3,");
        sb.Append(endDate);
        sb.Append(",4,5,1,5,0,5,1,5,1,5,1,5,1,6,6,5,1,6,6,2,3,2,");
        sb.Append(requestId);
        sb.Append(",7,8,7,9,7,10");

        return sb.ToString();
    }

    private static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
