using System.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public enum EvidenceValueKind { Null, Boolean, WholeNumber, Real, Text, Identifier, Timestamp }
