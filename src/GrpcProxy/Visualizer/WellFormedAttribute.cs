using System.ComponentModel.DataAnnotations;

namespace GrpcProxy.Visualizer;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public class WellFormedAttribute : ValidationAttribute
{
    public bool AllowEmptyStrings => false;

    public override bool IsValid(object? value) => value switch
    {
        string address => Uri.IsWellFormedUriString(address, UriKind.Absolute),
        _ => false
    };

    public override string FormatErrorMessage(string name) => $"{name} is not a well formed absolute url.";
}
