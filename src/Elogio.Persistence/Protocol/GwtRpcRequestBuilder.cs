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
    /// <param name="employeeId">The employee ID (default 227 from captures)</param>
    public string BuildGetSemaineRequest(string sessionId, int date, int employeeId = 227)
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
        // 0,1,2,2,20260105,3,4,227,5,6,5,7,5,8
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
    /// <param name="employeeId">The employee ID</param>
    public string BuildGetTraductionsRequest(string sessionId, string prefix, int employeeId = 227)
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

        // Data tokens from browser: 0,1,1,2,3,4,227,2,5,2,6,2,7
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
