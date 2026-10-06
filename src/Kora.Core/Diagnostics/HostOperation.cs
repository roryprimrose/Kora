using System.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public enum HostOperation { Startup, Request, Policy, Runtime, Tool, Storage, Presentation, Evidence, Retention, Recovery }
