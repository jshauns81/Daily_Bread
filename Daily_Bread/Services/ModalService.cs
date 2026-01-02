namespace Daily_Bread.Services;

using Microsoft.AspNetCore.Components;

/// <summary>
/// Service for managing modal dialogs rendered at the layout root level.
/// This ensures modals properly cover all UI elements including sidebars and headers.
/// </summary>
public class ModalService
{
    public event Action? OnChange;
    
    public bool IsOpen { get; private set; }
    public RenderFragment? Content { get; private set; }
    public bool CloseOnBackdropClick { get; private set; } = true;
    
    /// <summary>
    /// Opens a modal with the specified content.
    /// </summary>
    /// <param name="content">The RenderFragment to display in the modal.</param>
    /// <param name="closeOnBackdropClick">Whether clicking the backdrop closes the modal.</param>
    public void Open(RenderFragment content, bool closeOnBackdropClick = true)
    {
        Content = content;
        CloseOnBackdropClick = closeOnBackdropClick;
        IsOpen = true;
        OnChange?.Invoke();
    }
    
    /// <summary>
    /// Closes the currently open modal.
    /// </summary>
    public void Close()
    {
        IsOpen = false;
        Content = null;
        OnChange?.Invoke();
    }
}
