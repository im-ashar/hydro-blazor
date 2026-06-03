#if NET8_0_OR_GREATER

using Microsoft.AspNetCore.Components;

namespace Hydro.Blazor;

public class HydroBlazorContext
{
    public IComponent? ComponentInstance { get; set; }
}
#endif
