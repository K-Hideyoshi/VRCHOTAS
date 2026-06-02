using VRCHOTAS.Logging;
using VRCHOTAS.Services;

var parentProcessId = TryGetParentProcessId(args);
var logger = new FileAppLogger(fileNameSuffix: "overlay-helper");
using var host = new OverlayHelperHost(logger, new OpenVrNativeLibraryService(logger), parentProcessId);
await host.RunAsync();

static int? TryGetParentProcessId(string[] arguments)
{
    for (var i = 0; i < arguments.Length - 1; i++)
    {
        if (string.Equals(arguments[i], "--parent-pid", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(arguments[i + 1], out var processId))
        {
            return processId;
        }
    }

    return null;
}
