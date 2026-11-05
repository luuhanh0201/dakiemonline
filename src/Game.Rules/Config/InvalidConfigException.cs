using System;

namespace Game.Rules
{
    /// <summary>A config file was rejected; the message names the file, the field and the problem.</summary>
    public sealed class InvalidConfigException : Exception
    {
        public InvalidConfigException(string file, string path, string problem)
            : base($"{file}: {path}: {problem}")
        {
            File = file;
            FieldPath = path;
            Problem = problem;
        }

        public string File { get; }
        public string FieldPath { get; }
        public string Problem { get; }
    }
}
