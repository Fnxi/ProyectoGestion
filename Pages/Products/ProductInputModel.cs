using System.ComponentModel.DataAnnotations;
using WebApp.Models;

namespace WebApp.Pages.Products;

public sealed class ProductInputModel : IValidatableObject
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150, ErrorMessage = "El nombre no puede superar los 150 caracteres.")]
    [Display(Name = "Nombre")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "La descripción no puede superar los 1000 caracteres.")]
    [Display(Name = "Descripción")]
    public string? Description { get; set; }

    [Range(typeof(decimal), "0", "79228162514264337593543950335", ErrorMessage = "El precio no puede ser negativo.")]
    [Display(Name = "Precio")]
    public decimal Price { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "El stock no puede ser negativo.")]
    [Display(Name = "Stock")]
    public int Stock { get; set; }

    public static ProductInputModel FromProduct(Product product) => new()
    {
        Name = product.Name,
        Description = product.Description,
        Price = product.Price,
        Stock = product.Stock
    };

    public void ApplyTo(Product product)
    {
        product.Name = Name.Trim();
        product.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        product.Price = Price;
        product.Stock = Stock;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult("El nombre es obligatorio.", [nameof(Name)]);
        }
    }
}
