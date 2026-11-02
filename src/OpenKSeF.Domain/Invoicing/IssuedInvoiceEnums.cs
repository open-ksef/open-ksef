namespace OpenKSeF.Domain.Invoicing;

public enum IssuedInvoiceStatus
{
    Draft,
    Queued,
    Sending,
    Accepted,
    Rejected
}

public enum BuyerKind
{
    Company,
    Person
}

public enum PaymentMethod
{
    Transfer,
    Cash,
    Card
}

public enum ExemptionBasisKind
{
    Act,
    Directive,
    Other
}
