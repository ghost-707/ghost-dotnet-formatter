using GhostFormatter.Abstractions.Enums;

namespace GhostFormatter.Core.Modals;

internal sealed record SqlToken(SqlTokenType Type, string Value, bool PrecededByNewline = false);

