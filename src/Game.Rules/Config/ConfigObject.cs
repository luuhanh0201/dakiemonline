using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Rules
{
    /// <summary>
    /// Reads one JSON object field by field. Every failure names the file and field, and
    /// fields nobody read are rejected so a misspelled optional field cannot pass silently.
    /// </summary>
    internal sealed class ConfigObject
    {
        private const NumberStyles NumberFormat = NumberStyles.Float;

        private readonly string file;
        private readonly string path;
        private readonly JsonNode node;
        private readonly HashSet<string> read = new HashSet<string>();

        private ConfigObject(string file, string path, JsonNode node)
        {
            this.file = file;
            this.path = path;
            this.node = node;
        }

        public string Path => path;

        public static ConfigObject Root(string file, string json)
        {
            JsonNode root = JsonParser.Parse(file, json);

            if (root.Kind != JsonKind.Object)
            {
                throw new InvalidConfigException(file, "root", "must be an object");
            }

            return new ConfigObject(file, "root", root);
        }

        public bool Has(string name) => node.Fields.ContainsKey(name);

        public string String(string name) => Field(name, JsonKind.String, "a string").Text;

        public bool Boolean(string name) => Field(name, JsonKind.Boolean, "true or false").Text == "true";

        public decimal Decimal(string name)
        {
            string text = Field(name, JsonKind.Number, "a number").Text;

            if (decimal.TryParse(text, NumberFormat, CultureInfo.InvariantCulture, out decimal value))
            {
                return value;
            }

            throw Fail(name, $"number {text} is out of range");
        }

        public double Double(string name)
        {
            string text = Field(name, JsonKind.Number, "a number").Text;
            double value = double.Parse(text, NumberFormat, CultureInfo.InvariantCulture);

            if (double.IsInfinity(value))
            {
                throw Fail(name, $"number {text} is out of range");
            }

            return value;
        }

        public int Int(string name)
        {
            decimal value = Decimal(name);

            if (value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
            {
                throw Fail(name, "must be a whole number");
            }

            return (int)value;
        }

        public double Positive(string name)
        {
            double value = Double(name);
            return value > 0d ? value : throw Fail(name, "must be greater than 0");
        }

        public double NotNegative(string name)
        {
            double value = Double(name);
            return value >= 0d ? value : throw Fail(name, "cannot be negative");
        }

        public decimal NotNegativeDecimal(string name)
        {
            decimal value = Decimal(name);
            return value >= 0m ? value : throw Fail(name, "cannot be negative");
        }

        public decimal PositiveDecimal(string name)
        {
            decimal value = Decimal(name);
            return value > 0m ? value : throw Fail(name, "must be greater than 0");
        }

        public double Share(string name)
        {
            double value = Double(name);
            return value > 0d && value < 1d ? value : throw Fail(name, "must be between 0 and 1");
        }

        public double Percent(string name)
        {
            double value = Double(name);
            return value >= 0d && value <= 100d ? value : throw Fail(name, "must be from 0 to 100");
        }

        public ConfigObject Object(string name)
        {
            return new ConfigObject(file, $"{path}.{name}", Field(name, JsonKind.Object, "an object"));
        }

        public IReadOnlyList<ConfigObject> Array(string name)
        {
            JsonNode array = Field(name, JsonKind.Array, "an array");
            var items = new List<ConfigObject>(array.Items.Count);

            for (int index = 0; index < array.Items.Count; index++)
            {
                string itemPath = $"{path}.{name}[{index}]";

                if (array.Items[index].Kind != JsonKind.Object)
                {
                    throw new InvalidConfigException(file, itemPath, "must be an object");
                }

                items.Add(new ConfigObject(file, itemPath, array.Items[index]));
            }

            return items;
        }

        public InvalidConfigException Fail(string name, string problem)
        {
            return new InvalidConfigException(file, $"{path}.{name}", problem);
        }

        /// <summary>Call after reading every field.</summary>
        public void RejectUnknownFields()
        {
            foreach (string name in node.Fields.Keys)
            {
                if (read.Contains(name) == false)
                {
                    throw Fail(name, "unknown field");
                }
            }
        }

        private JsonNode Field(string name, JsonKind kind, string expected)
        {
            read.Add(name);

            if (node.Fields.TryGetValue(name, out JsonNode? value) == false)
            {
                throw Fail(name, "missing field");
            }

            return value.Kind == kind ? value : throw Fail(name, $"must be {expected}");
        }
    }
}
