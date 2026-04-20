using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace AssetHub.Application.Validation;

/// <summary>
/// Validation attribute that caps the number of items in a collection. Drop-in
/// replacement for <see cref="MaxLengthAttribute"/> on <see cref="IEnumerable"/>
/// properties — <see cref="ValidationFilter{T}"/> picks it up via
/// <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult})"/>,
/// exactly like any other <see cref="ValidationAttribute"/>.
///
/// Use this instead of <see cref="MaxLengthAttribute"/> on <b>nullable</b> collection
/// properties (<c>List&lt;T&gt;?</c>). The .NET 9 OpenAPI schema generator crashes when it
/// encounters <see cref="MaxLengthAttribute"/> on a nullable collection because it emits
/// <c>type: ["array", "null"]</c> and then assumes the <c>type</c> keyword is a single
/// string. Custom attribute types are ignored by the generator's validation-attribute
/// switch and sidestep the bug entirely.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class MaxItemsAttribute : ValidationAttribute
{
    public int MaxItems { get; }

    public MaxItemsAttribute(int maxItems)
    {
        if (maxItems < 0) throw new ArgumentOutOfRangeException(nameof(maxItems));
        MaxItems = maxItems;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null) return ValidationResult.Success;
        if (value is not IEnumerable enumerable) return ValidationResult.Success;

        var count = 0;
        foreach (var _ in enumerable)
        {
            count++;
            if (count > MaxItems)
            {
                var name = validationContext.DisplayName ?? validationContext.MemberName ?? "Collection";
                return new ValidationResult(
                    FormatErrorMessage(name),
                    validationContext.MemberName is { } m ? new[] { m } : null);
            }
        }

        return ValidationResult.Success;
    }

    public override string FormatErrorMessage(string name) =>
        ErrorMessage ?? $"The field {name} must contain no more than {MaxItems} items.";
}
