using Garage.Sim.Tools;

namespace Garage.Game
{
    /// <summary>Why a command could not run.</summary>
    public enum CommandError
    {
        /// <summary>No error.</summary>
        None,

        /// <summary>Not enough money.</summary>
        NotEnoughMoney,

        /// <summary>Tool or upgrade not owned.</summary>
        ToolLocked,

        /// <summary>Part does not fit that component or car.</summary>
        IncompatiblePart,

        /// <summary>Part not in stock.</summary>
        NotInStock,

        /// <summary>Unknown id.</summary>
        NotFound,

        /// <summary>No car/job on the lift.</summary>
        NoActiveJob,

        /// <summary>Wrong state (e.g. job not accepted, scanner not plugged).</summary>
        InvalidState,

        /// <summary>Reputation too low.</summary>
        ReputationTooLow,

        /// <summary>Invalid argument.</summary>
        InvalidArgument,
    }

    /// <summary>Outcome of a player command with a readable message.</summary>
    public sealed class CommandResult
    {
        private CommandResult(bool ok, string message, CommandError error, ToolResult? tool)
        {
            Ok = ok;
            Message = message;
            Error = error;
            Tool = tool;
        }

        /// <summary>Succeeded.</summary>
        public bool Ok { get; }

        /// <summary>Readable text (result display or error reason).</summary>
        public string Message { get; }

        /// <summary>Error code.</summary>
        public CommandError Error { get; }

        /// <summary>Underlying tool result, if any.</summary>
        public ToolResult? Tool { get; }

        /// <summary>Success.</summary>
        public static CommandResult Success(string message, ToolResult? tool = null) => new CommandResult(true, message, CommandError.None, tool);

        /// <summary>Success from a tool result (a tool can "work" and still report a problem, e.g. no response).</summary>
        public static CommandResult From(ToolResult r) => new CommandResult(r.Ok, r.Display, r.Ok ? CommandError.None : CommandError.InvalidState, r);

        /// <summary>Failure.</summary>
        public static CommandResult Fail(CommandError error, string message) => new CommandResult(false, message, error, null);

        /// <inheritdoc />
        public override string ToString() => Message;
    }
}
