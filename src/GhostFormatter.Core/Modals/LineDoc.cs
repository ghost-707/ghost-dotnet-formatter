using GhostFormatter.Abstractions.Enums;

namespace GhostFormatter.Core.Modals;

public sealed record LineDoc(LineKind Kind) : Doc;
