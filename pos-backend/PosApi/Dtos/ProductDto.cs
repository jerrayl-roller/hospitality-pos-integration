namespace PosApi.Dtos;

public record ProductDto(
    string ProductId,
    string Name,
    decimal Price,
    string ProductType,
    string ProductSubType,
    string? Category
);
