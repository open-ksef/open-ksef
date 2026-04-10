using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Domain.Aggregates;

namespace OpenKSeF.Invoices.Application.Commands.CreateCorrectionFromOriginal;

public interface ICreateCorrectionFromOriginalHandler
{
    Invoice Handle(Invoice original, CreateCorrectionFromOriginalCommand command);
}
