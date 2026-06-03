#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

namespace Hydro.Blazor;

public abstract class HydroBlazorComponent : ComponentBase
{
    [Inject]
    public HydroBlazorContext HydroContext { get; set; } = default!;

    [Parameter]
    public string HydroId { get; set; } = Guid.NewGuid().ToString("N");

    [Parameter]
    public string? HydroName { get; set; }

    [Parameter]
    public string? HydroActionName { get; set; }

    [Parameter]
    public Dictionary<string, object>? HydroActionParameters { get; set; }

    [Parameter]
    public Dictionary<string, object>? HydroState { get; set; }

    [Parameter]
    public Dictionary<string, object>? HydroFormValues { get; set; }

    public override async Task SetParametersAsync(ParameterView parameters)
    {
        await base.SetParametersAsync(parameters);
        
        HydroContext.ComponentInstance = this;

        if (string.IsNullOrEmpty(HydroName))
        {
            HydroName = GetType().Name;
        }

        // Hydrate state
        if (HydroState != null)
        {
            var properties = GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (prop.GetCustomAttribute<ParameterAttribute>() != null || prop.GetCustomAttribute<InjectAttribute>() != null)
                {
                    continue;
                }

                if (HydroState.TryGetValue(prop.Name, out var val))
                {
                    if (prop.CanWrite)
                    {
                        var converted = val;
                        if (val != null)
                        {
                            try
                            {
                                if (val is Newtonsoft.Json.Linq.JToken jToken)
                                {
                                    converted = jToken.ToObject(prop.PropertyType);
                                }
                                else
                                {
                                    converted = Convert.ChangeType(val, prop.PropertyType);
                                }
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                        prop.SetValue(this, converted);
                    }
                }
            }
        }

        // Apply form values
        if (HydroFormValues != null)
        {
            foreach (var pair in HydroFormValues)
            {
                var setterInfo = PropertyInjector.GetPropertySetter(this, pair.Key, pair.Value);
                if (setterInfo != null)
                {
                    setterInfo.Value.Setter(setterInfo.Value.Value);
                }
            }
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        if (!string.IsNullOrEmpty(HydroActionName))
        {
            var method = GetType().GetMethod(HydroActionName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (method != null)
            {
                var methodParams = method.GetParameters();
                var args = new object[methodParams.Length];
                
                for (int i = 0; i < methodParams.Length; i++)
                {
                    var param = methodParams[i];
                    if (HydroActionParameters != null && HydroActionParameters.TryGetValue(param.Name, out var val))
                    {
                        if (val is Newtonsoft.Json.Linq.JToken jToken)
                        {
                            args[i] = jToken.ToObject(param.ParameterType);
                        }
                        else
                        {
                            args[i] = val != null ? Convert.ChangeType(val, param.ParameterType) : null;
                        }
                    }
                }

                if (typeof(Task).IsAssignableFrom(method.ReturnType))
                {
                    await (Task)method.Invoke(this, args)!;
                }
                else
                {
                    method.Invoke(this, args);
                }
            }
        }
    }
}
#endif
