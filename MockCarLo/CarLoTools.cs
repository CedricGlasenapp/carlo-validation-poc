using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace MockCarLo;

/// <summary>
/// The three CarLo functions the mock exposes as MCP tools. Two read, one
/// writes. The annotations (ReadOnly, Destructive) are what the real server
/// would declare; the validation layer deliberately does not trust them
/// (MCP specification: annotations are hints, not security guarantees).
/// </summary>
[McpServerToolType]
public sealed class CarLoTools(CarLoStore store)
{
    [McpServerTool(Name = "GetTransportOrder", ReadOnly = true, Idempotent = true)]
    [Description("Returns a transport order with its header data and information field.")]
    public string GetTransportOrder(
        [Description("Order number, e.g. TO-2026-0815")] string orderId)
    {
        return store.Orders.TryGetValue(orderId, out var order)
            ? JsonSerializer.Serialize(order)
            : $"error: order {orderId} not found";
    }

    [McpServerTool(Name = "ReadDocument", ReadOnly = true, Idempotent = true)]
    [Description("Returns the text of an inbound document (e-mail, PDF) attached to an order.")]
    public string ReadDocument(
        [Description("Document id, e.g. DOC-4711")] string documentId)
    {
        return store.Documents.TryGetValue(documentId, out var doc)
            ? doc.Text
            : $"error: document {documentId} not found";
    }

    [McpServerTool(Name = "UpdateCustomerBankAccount", Destructive = true)]
    [Description("Changes the bank account (IBAN) in a customer's master record.")]
    public string UpdateCustomerBankAccount(
        [Description("Customer id, e.g. K-1001")] string customerId,
        [Description("New IBAN")] string iban)
    {
        if (!store.Customers.TryGetValue(customerId, out var customer))
            return $"error: customer {customerId} not found";

        store.Customers[customerId] = customer with { Iban = iban };
        var change = $"{DateTime.UtcNow:O} {customerId} IBAN {customer.Iban} -> {iban}";
        store.Changes.Add(change);
        Console.WriteLine($"[mock-carlo] WRITE {change}");
        return $"ok: {customerId} IBAN set to {iban}";
    }
}
