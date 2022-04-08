using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace OtterGui.Filesystem;

public partial class FileSystem<T>
{
    public class Folder : IWritePath
    {
        internal const byte RootDepth = byte.MaxValue;

        // The folder internally keeps track of total descendants and total leaves.
        public int TotalDescendants { get; internal set; } = 0;
        public int TotalLeaves      { get; internal set; } = 0;

        internal List<IWritePath> Children = new();

        public int TotalChildren
            => Children.Count;

        public   Folder      Parent        { get; internal set; }
        public   string      Name          { get; private set; }
        public   uint        Identifier    { get; }
        public   ushort      IndexInParent { get; internal set; }
        public   byte        Depth         { get; internal set; }

        public bool IsRoot
            => Depth == RootDepth;

        void IWritePath.SetParent(Folder parent)
            => Parent = parent;

        void IWritePath.SetName(string name, bool fix)
            => Name = fix ? name.FixName() : name;

        void IWritePath.UpdateDepth()
        {
            var oldDepth = Depth;
            Depth = unchecked((byte)(Parent.Depth + 1));
            if (Depth == oldDepth)
                return;

            foreach (var desc in GetWriteDescendants())
                desc.UpdateDepth();
        }

        void IWritePath.UpdateIndex(int index)
        {
            if (index < 0)
                index = Parent.Children.IndexOf(this);
            IndexInParent = (ushort)(index < 0 ? 0 : index);
        }

        public Folder(Folder parent, string name, uint identifier)
        {
            Parent     = parent;
            Name       = name.FixName();
            Identifier = identifier;
        }

        // Iterate through all direct children in sort order.
        public IEnumerable<IPath> GetChildren(SortMode mode)
        {
            switch (mode)
            {
                case SortMode.FoldersFirst:
                    foreach (var child in Children.OfType<Folder>())
                        yield return child;
                    foreach (var child in Children.OfType<Leaf>())
                        yield return child;

                    break;
                case SortMode.FoldersLast:
                    foreach (var child in Children.OfType<Leaf>())
                        yield return child;
                    foreach (var child in Children.OfType<Folder>())
                        yield return child;

                    break;
                case SortMode.Lexicographical:
                    foreach (var child in Children)
                        yield return child;

                    break;
                case SortMode.InverseFoldersFirst:
                    foreach (var child in Children.OfType<Folder>().Reverse())
                        yield return child;
                    foreach (var child in Children.OfType<Leaf>().Reverse())
                        yield return child;

                    break;
                case SortMode.InverseLexicographical:
                    foreach (var child in ((IReadOnlyList<IPath>)Children).Reverse())
                        yield return child;

                    break;
                case SortMode.InverseFoldersLast:
                    foreach (var child in Children.OfType<Leaf>().Reverse())
                        yield return child;
                    foreach (var child in Children.OfType<Folder>().Reverse())
                        yield return child;


                    break;
                default: throw new InvalidEnumArgumentException();
            }
        }

        // Iterate through all Descendants in sort order, not including the folder itself.
        public IEnumerable<IPath> GetAllDescendants(SortMode mode)
        {
            return GetChildren(mode).SelectMany(p => p is Folder f
                ? f.GetAllDescendants(mode).Prepend(f)
                : Array.Empty<IPath>().Append(p));
        }

        internal IEnumerable<IWritePath> GetWriteDescendants()
        {
            return Children.SelectMany(p => p is Folder f
                ? f.GetWriteDescendants().Prepend(f)
                : Array.Empty<IWritePath>().Append(p));
        }

        public string FullName()
            => IPath.BaseFullName(this);

        public override string ToString()
            => FullName();


        // Creates the specific root element.
        // The name is set to empty due to it being fixed in the constructor.
        internal static Folder CreateRoot()
            => new(null!, "_", 0)
            {
                Name  = string.Empty,
                Depth = RootDepth,
            };
    }
}
