using ClassifiedAds.CrossCuttingConcerns.Csv;
using ClassifiedAds.Domain.Entities;
using System.Collections.Generic;

namespace ClassifiedAds.Application.Products.DTOs;

public record ExportProductsToCsv : ICsvRequest
{
    public List<Product> Products { get; set; }
}
