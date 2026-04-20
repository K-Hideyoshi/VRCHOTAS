using VRCHOTAS.Models;

namespace VRCHOTAS.ViewModels;

public sealed class MappingEditorRequestEventArgs : EventArgs
{
    public MappingEditorRequestEventArgs(MappingEntry? mappingToEdit)
    {
        MappingToEdit = mappingToEdit;
    }

    public MappingEntry? MappingToEdit { get; }
}
