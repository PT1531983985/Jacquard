using System;
using System.Collections.Generic;

namespace Jacquard {

// The identity of an open file is inseparable from its editing buffer. Choosing
// another tab never retargets Save on a project belonging to the previous one.
public sealed class ScoreDocument
{
    public string Name { get; }
    public Project Project { get; }
    public string DiskText { get; private set; }
    string _savedState;
    public GridPoint Cursor { get; set; } = new GridPoint(1, 1);
    public bool HasBeenShown { get; private set; }

    public ScoreDocument(string name, Project project, string diskText = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A document needs a name");
        Name = name;
        Project = project ?? throw new ArgumentNullException(nameof(project));
        DiskText = diskText;
        _savedState = diskText == null ? null : ProjectFormat.Write(project);
    }

    // Comparing canonical text includes mix changes, which do not all raise the
    // score editor's Changed event. Switching back also detects a reverted edit.
    public bool IsDirty => _savedState == null || ProjectFormat.Write(Project) != _savedState;

    // The score plane normalizes coordinates on its first rebuild. That view
    // adjustment is the loaded baseline, not an edit; the original disk text is
    // retained separately for detecting changes made outside this session.
    public void ShownOnPlane()
    {
        if (HasBeenShown) return;
        HasBeenShown = true;
        if (DiskText != null) _savedState = ProjectFormat.Write(Project);
    }

    public void MarkSaved(string text)
    {
        DiskText = text;
        _savedState = text;
    }
}

public sealed class ScoreDocuments
{
    readonly List<ScoreDocument> _open = new();
    readonly IReadOnlyList<ScoreDocument> _view;
    public ScoreDocuments() => _view = _open.AsReadOnly();
    public IReadOnlyList<ScoreDocument> Open => _view;
    public ScoreDocument Active { get; private set; }

    public ScoreDocument Find(string name)
      => _open.Find(document => string.Equals(document.Name, name, StringComparison.OrdinalIgnoreCase));

    public void Add(ScoreDocument document)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (Find(document.Name) != null) throw new ArgumentException("The document is already open");
        _open.Add(document);
    }

    public void Activate(ScoreDocument document)
    {
        if (!_open.Contains(document)) throw new ArgumentException("The document is not open");
        Active = document;
    }
}

} // namespace Jacquard
