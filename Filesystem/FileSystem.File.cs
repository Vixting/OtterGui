using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OtterGui.Filesystem;

public partial class FileSystem<T>
{
    // Save a current filesystem to a file, using a function that transforms the data value as well as its full path
    // to a identifier string for the data as well as a bool whether this data should be saved.
    // If addEmptyFolders is true, folders without any leaves are stored separately.
    protected void SaveToFile(FileInfo file, Func<T, string, (string, bool)> conversion, bool addEmptyFolders)
    {
        using var stream = File.Exists(file.FullName)
            ? File.Open(file.FullName, FileMode.Truncate)
            : File.Open(file.FullName, FileMode.CreateNew);
        using var w = new StreamWriter(stream);
        using var j = new JsonTextWriter(w);
        j.Formatting = Formatting.Indented;

        var emptyFolders = new List<string>();
        j.WriteStartObject();
        j.WritePropertyName("Data");
        j.WriteStartObject();
        // Iterate lexicographically through all decendants, keep track of empty folders if necessary.
        // otherwise write all the paths that are given by the conversion function.
        if (Root.Children.Count > 0)
            foreach (var path in Root.GetAllDescendants(SortMode.Lexicographical))
            {
                switch (path)
                {
                    case Folder f:
                        if (addEmptyFolders && f.Children.Count == 0)
                            emptyFolders.Add(f.FullName());
                        break;
                    case Leaf l:
                        var fullPath = l.FullName();
                        var (name, write) = conversion(l.Value, fullPath);
                        if (write)
                        {
                            j.WritePropertyName(name);
                            j.WriteValue(fullPath);
                        }

                        break;
                }
            }

        j.WriteEndObject();
        // Write empty folders if applicable.
        if (addEmptyFolders)
        {
            j.WritePropertyName("EmptyFolders");
            j.WriteStartArray();
            foreach (var emptyFolder in emptyFolders)
                j.WriteValue(emptyFolder);
            j.WriteEndArray();
        }

        j.WriteEndObject();
    }

    // Check if a path ends in a duplicate number already and increment if so, otherwise add (2).
    private static readonly Regex DuplicateRegex = new(@"\((?'Number'\d+)\)$", RegexOptions.Compiled);

    private static string FixDuplicateName(string name)
    {
        var match = DuplicateRegex.Match(name);
        return $"{name} ({(match.Success ? int.Parse(match.Groups["Number"].Value) + 1 : 2)})";
    }

    // Load a given FileSystem from file, using an enumeration of data values, a function that corresponds a data value to its identifier
    // and a function that corresponds a data value not stored in the saved filesystem to its name.
    protected bool Load(FileInfo file, IEnumerable<T> objects, Func<T, string> toIdentifier, Func<T, string> toName)
    {
        IdCounter = 1;
        Root.Children.Clear();
        if (!File.Exists(file.FullName))
            return true;

        var changes = false;
        try
        {
            var jObject      = JObject.Parse(File.ReadAllText(file.FullName));
            var data         = jObject["Data"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>();
            var emptyFolders = jObject["EmptyFolders"]?.ToObject<string[]>() ?? Array.Empty<string>();

            foreach (var value in objects)
            {
                var identifier = toIdentifier(value);
                // If the data has a path in the filesystem, create all necessary folders and set the leaf.
                if (data.TryGetValue(identifier, out var path))
                {
                    data.Remove(identifier);
                    var split = path.SplitDirectories();
                    var (result, folder) = CreateAllFolders(split[..^1]);
                    if (result is not Result.Success and not Result.SuccessNothingDone)
                    {
                        changes = true;
                        continue;
                    }

                    var leaf = new Leaf(folder, split[^1], value, IdCounter++);
                    while (SetChild(folder, leaf, out _) == Result.ItemExists)
                    {
                        leaf.SetName(FixDuplicateName(leaf.Name));
                        changes = true;
                    }
                }
                else
                {
                    // Add a new leaf using the given toName function.
                    var leaf = new Leaf(Root, toName(value), value, IdCounter++);
                    while (SetChild(Root, leaf, out _) == Result.ItemExists)
                    {
                        leaf.SetName(FixDuplicateName(leaf.Name));
                        changes = true;
                    }
                }
            }

            // Add all empty folders and potential unfinished folders from 
            foreach (var split in emptyFolders.Concat(data.Values).Select(folder => folder.SplitDirectories()))
            {
                var (result, _) = CreateAllFolders(split);
                if (result is not Result.Success and not Result.SuccessNothingDone)
                    changes = true;
            }
        }
        catch
        {
            return true;
        }

        return changes;
    }
}
