namespace PosApi.Dtos;

public record ProductDto(
    string ProductId,
    string Name,
    string ParentName,
    decimal Price,
    string ProductType,
    string ProductSubType,
    string? Category,
    string? ImageUrl
);
