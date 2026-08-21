using System;

namespace MonKineBlazor.Client;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ModuleAccessAttribute : Attribute
{
    public string ModuleId { get; }

    public ModuleAccessAttribute(string moduleId)
    {
        ModuleId = moduleId;
    }
}
