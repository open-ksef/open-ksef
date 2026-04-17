using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Filters;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;
using OpenKSeF.Invoices.Application;
using OpenKSeF.Invoices.Application.Commands.ApproveInvoice;
using OpenKSeF.Invoices.Application.Commands.CreateCorrectionFromOriginal;
using OpenKSeF.Invoices.Application.Commands.CreateFinalInvoiceFromAdvances;
using OpenKSeF.Invoices.Application.Commands.CreateInvoice;
using OpenKSeF.Invoices.Application.Commands.RecordKsefAcceptance;
using OpenKSeF.Invoices.Application.Commands.RecordKsefRejection;
using OpenKSeF.Invoices.Application.Commands.ReopenInvoice;
using OpenKSeF.Invoices.Application.Commands.UpdateInvoiceDraft;
using OpenKSeF.Invoices.Application.Projection;
using OpenKSeF.Invoices.Contracts.Commands;
using OpenKSeF.Invoices.Contracts.Dtos;
using OpenKSeF.Invoices.Contracts.Dtos.Requests;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Exceptions;
using OpenKSeF.Invoices.Domain.Integration;
using OpenKSeF.Invoices.Domain.Persistence;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Domain.Projection;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.Validation;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.ValueObjects;
using OpenKSeF.Invoices.Infrastructure;

namespace OpenKSeF.Api.Tests.Controllers;

public sealed class InvoicesAggregateControllerContractTests
{
    [Fact]
    public async Task Api001_ListAggregateInvoicesByStatusFilter()
    {
        using var harness = new ContractHarness();
        await harness.SaveInvoiceAsync(MakeDraftInvoice(harness.TenantId, "FV/DRAFT"));
        await harness.SaveInvoiceAsync(MakeAcceptedInvoice(harness.TenantId, "FV/ACCEPTED"));

        var result = await harness.ExecuteAsync(controller =>
            controller.List(harness.TenantId, ["Draft"], null, null, null, null, 1, 25));

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<PagedResult<InvoiceReadDto>>(ok.Value);
        var item = Assert.Single(page.Items);
        Assert.Equal("Draft", item.Status);
        Assert.Equal("VatInvoice", item.Kind);
        Assert.Equal("NotPlanned", item.KsefSubmissionState);
    }

    [Fact]
    public async Task Api002_ListRejectsUnknownStatusFilter()
    {
        using var harness = new ContractHarness();

        var result = await harness.ExecuteAsync(controller =>
            controller.List(harness.TenantId, ["NonExisting"], null, null, null, null, 1, 25));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<RuleCodeErrorResponse>(badRequest.Value);
        Assert.Equal("INV-VAL-003", error.Code);
    }

    [Fact]
    public async Task Api003_GetDetailReturnsInvoiceReadDto()
    {
        using var harness = new ContractHarness();
        var invoice = MakeApprovedInvoice(harness.TenantId, "FV/DETAIL");
        await harness.SaveInvoiceAsync(invoice);

        var result = await harness.ExecuteAsync(controller =>
            controller.Get(harness.TenantId, invoice.Id.Value));

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<InvoiceReadDto>(ok.Value);
        Assert.Equal("Approved", dto.Status);
        Assert.Equal("VatInvoice", dto.Kind);
        Assert.Equal("Business", dto.BuyerKind);
    }

    [Fact]
    public async Task Api004_CreateDraftHappyPath()
    {
        using var harness = new ContractHarness();

        var request = new CreateInvoiceRequest(
            DocumentKind.VatInvoice,
            "Seller",
            "1234567890",
            "Buyer",
            BuyerKind.Business,
            "9876543210",
            "PLN",
            new DateTime(2026, 4, 10),
            KsefSubmissionRequirement.Required,
            "FV/NEW/1",
            "EXT-1");

        var result = await harness.ExecuteAsync(controller =>
            controller.Create(harness.TenantId, request));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var dto = Assert.IsType<InvoiceReadDto>(created.Value);
        Assert.Equal(nameof(InvoicesAggregateController.Get), created.ActionName);
        Assert.Equal("Draft", dto.Status);
        Assert.Equal(harness.TenantId, dto.TenantId);
    }

    [Fact]
    public async Task Api005_CreateDraftReturnsValidationEnvelopeOnMissingSeller()
    {
        using var harness = new ContractHarness();

        var request = new CreateInvoiceRequest(
            DocumentKind.VatInvoice,
            "",
            "1234567890",
            "Buyer",
            BuyerKind.Business,
            "9876543210",
            "PLN",
            new DateTime(2026, 4, 10),
            KsefSubmissionRequirement.Required);

        var result = await harness.ExecuteAsync(controller =>
            controller.Create(harness.TenantId, request));

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var envelope = Assert.IsType<ValidationEnvelope>(unprocessable.Value);
        Assert.Equal("Draft", envelope.Stage);
        Assert.Contains(envelope.Messages, message => message.Code == "INV-VAL-010" && message.Severity == "Error");
    }

    [Fact]
    public async Task Api005a_CreateProformaReturnsValidationEnvelopeOnMissingSellerNip()
    {
        using var harness = new ContractHarness();

        var request = new CreateInvoiceRequest(
            DocumentKind.Proforma,
            "Seller",
            "",
            "Consumer",
            BuyerKind.Consumer,
            null,
            "PLN",
            new DateTime(2026, 4, 10),
            KsefSubmissionRequirement.Forbidden);

        var result = await harness.ExecuteAsync(controller =>
            controller.Create(harness.TenantId, request));

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var envelope = Assert.IsType<ValidationEnvelope>(unprocessable.Value);
        Assert.Equal("Draft", envelope.Stage);
        Assert.Contains(envelope.Messages, message => message.Code == "INV-VAL-011" && message.Severity == "Error");
    }

    [Fact]
    public async Task Api006_UpdateDraftIsIdempotentForNoOp()
    {
        using var harness = new ContractHarness();
        var invoice = MakeDraftInvoice(harness.TenantId, "FV/NOOP");
        await harness.SaveInvoiceAsync(invoice);

        var request = new UpdateInvoiceDraftRequest();

        var first = await harness.ExecuteAsync(controller =>
            controller.UpdateDraft(harness.TenantId, invoice.Id.Value, request));
        var second = await harness.ExecuteAsync(controller =>
            controller.UpdateDraft(harness.TenantId, invoice.Id.Value, request));

        Assert.IsType<OkObjectResult>(first);
        Assert.IsType<OkObjectResult>(second);

        var reloaded = await harness.Repository.FindByIdAsync(invoice.Id);
        Assert.NotNull(reloaded);
        Assert.Empty(reloaded!.DomainEvents);
    }

    [Fact]
    public async Task Api006a_UpdateDraftAcceptsPartialPatchForLinesAndReference()
    {
        using var harness = new ContractHarness();
        var invoice = MakeDraftInvoice(harness.TenantId, "FV/PATCH");
        invoice.AddLine(InvoiceLine.Create(
            2,
            "First",
            1m,
            new Money(100m, CurrencyCode.Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(8))));
        invoice.RecalculateTotals();
        await harness.SaveInvoiceAsync(invoice);

        var request = new UpdateInvoiceDraftRequest(
            ExternalReference: "ERP-77",
            Lines:
            [
                new UpdateInvoiceDraftLineRequest(1, "Second", 1m, "szt.", PricingMode.Net, 200m, null, "23%"),
                new UpdateInvoiceDraftLineRequest(2, "First", 1m, "szt.", PricingMode.Net, 100m, null, "8%")
            ]);

        var result = await harness.ExecuteAsync(controller =>
            controller.UpdateDraft(harness.TenantId, invoice.Id.Value, request));

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<InvoiceReadDto>(ok.Value);
        Assert.Equal("ERP-77", dto.ExternalReference);
        Assert.Collection(
            dto.Lines,
            line =>
            {
                Assert.Equal(1, line.LineNumber);
                Assert.Equal("Second", line.Description);
            },
            line =>
            {
                Assert.Equal(2, line.LineNumber);
                Assert.Equal("First", line.Description);
                Assert.Equal("8%", line.VatRate);
            });
    }

    [Fact]
    public async Task Api007_ApproveReturnsEnvelopeGroupedByStage()
    {
        using var harness = new ContractHarness();
        var invoice = MakeDraftInvoice(harness.TenantId, "FV/APPROVE-FAIL");
        await harness.SaveInvoiceAsync(invoice);

        var approveHandler = new ThrowingApproveInvoiceHandler(
            new ValidationResult(
            [
                new ValidationMessage("INV-VAL-060", ValidationSeverity.Error, ValidationStage.Approve, "INV-VAL-060", "INV-VAL-060", "LineItems[1].VatRate"),
                new ValidationMessage("INV-VAL-013", ValidationSeverity.Error, ValidationStage.Approve, "INV-VAL-013", "INV-VAL-013", "Buyer.Nip")
            ]));

        var result = await harness.ExecuteAsync(
            controller => controller.Approve(harness.TenantId, invoice.Id.Value, new ApproveInvoiceRequest()),
            approveHandler: approveHandler);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var envelope = Assert.IsType<ValidationEnvelope>(unprocessable.Value);
        Assert.Equal("Approve", envelope.Stage);
        Assert.Contains(envelope.Messages, message => message.Code == "INV-VAL-060" && message.Severity == "Error");
        Assert.Contains(envelope.Messages, message => message.Code == "INV-VAL-013" && message.Severity == "Error");
    }

    [Fact]
    public async Task Api008_ReopenHonorsApprovedEditPolicy()
    {
        using var harness = new ContractHarness();
        var invoice = MakeApprovedInvoice(harness.TenantId, "FV/REOPEN");
        await harness.SaveInvoiceAsync(invoice);

        var result = await harness.ExecuteAsync(
            controller => controller.Reopen(harness.TenantId, invoice.Id.Value, new ReopenInvoiceRequest()),
            reopenHandler: new ReopenInvoiceHandler(new NeverAllowReopenPolicy()));

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var envelope = Assert.IsType<ValidationEnvelope>(conflict.Value);
        Assert.Contains(envelope.Messages, message => message.Code == "INV-VAL-102");
    }

    [Fact]
    public async Task Api009_SubmitToKsefRequiresApprovedStatus()
    {
        using var harness = new ContractHarness();
        var invoice = MakeDraftInvoice(harness.TenantId, "FV/SUBMIT");
        await harness.SaveInvoiceAsync(invoice);
        await harness.AddKsefCredentialAsync();

        var mapper = Substitute.For<IInvoiceToKsefPayloadMapper>();
        mapper.TryMap(Arg.Any<Invoice>()).Returns(new KsefInvoicePayload("<Invoice />", "FV/SUBMIT", "1234567890"));

        var result = await harness.ExecuteAsync(
            controller => controller.SubmitToKsef(harness.TenantId, invoice.Id.Value),
            invoiceToKsefPayloadMapper: mapper);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var envelope = Assert.IsType<ValidationEnvelope>(conflict.Value);
        Assert.Contains(envelope.Messages, message => message.Code == "INV-VAL-100");
    }

    [Fact]
    public async Task Api010_CreateCorrectionFromOriginalProducesDraft()
    {
        using var harness = new ContractHarness();
        var original = MakeAcceptedInvoice(harness.TenantId, "FV/ORIGINAL");
        await harness.SaveInvoiceAsync(original);

        var request = new CreateCorrectionFromOriginalRequest(
            new DateTime(2026, 4, 11),
            CorrectionReasonKind.ValueChange,
            "Price correction");

        var result = await harness.ExecuteAsync(controller =>
            controller.CreateCorrection(harness.TenantId, original.Id.Value, request));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var dto = Assert.IsType<InvoiceReadDto>(created.Value);
        Assert.Equal("CorrectionInvoice", dto.Kind);
        Assert.Equal("Draft", dto.Status);
        Assert.Equal(original.Id.Value, dto.CorrectionReference?.OriginalInvoiceId);
    }

    [Fact]
    public async Task Api011_FinalFromAdvancesRejectsMixedBuyers()
    {
        using var harness = new ContractHarness();
        var first = MakeAdvanceInvoice(harness.TenantId, "ADV/1", "9876543210");
        var second = MakeAdvanceInvoice(harness.TenantId, "ADV/2", "1111111111");
        await harness.SaveInvoiceAsync(first);
        await harness.SaveInvoiceAsync(second);

        var request = new CreateFinalInvoiceFromAdvancesRequest(
            new DateTime(2026, 4, 12),
            [
                new AdvanceSettlementEntryRequest(first.Id.Value, first.DocumentNumber!.Value, 50m),
                new AdvanceSettlementEntryRequest(second.Id.Value, second.DocumentNumber!.Value, 50m)
            ]);

        var result = await harness.ExecuteAsync(controller =>
            controller.CreateFinalFromAdvances(harness.TenantId, request));

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var envelope = Assert.IsType<ValidationEnvelope>(unprocessable.Value);
        Assert.Contains(envelope.Messages, message => message.Code == "INV-VAL-073");
    }

    [Fact]
    public async Task Api012_PrintEndpointReturnsEnglishLabelsOnVariantEnglish()
    {
        using var harness = new ContractHarness();
        var invoice = MakeAcceptedInvoice(harness.TenantId, "FV/PRINT");
        await harness.SaveInvoiceAsync(invoice);

        var standardResult = await harness.ExecuteAsync(controller =>
            controller.GetPrint(harness.TenantId, invoice.Id.Value, nameof(PrintVariant.Standard)));
        var englishResult = await harness.ExecuteAsync(controller =>
            controller.GetPrint(harness.TenantId, invoice.Id.Value, nameof(PrintVariant.English)));

        var standard = Assert.IsType<InvoicePrintModel>(Assert.IsType<OkObjectResult>(standardResult).Value);
        var english = Assert.IsType<InvoicePrintModel>(Assert.IsType<OkObjectResult>(englishResult).Value);
        Assert.Equal(PrintVariant.English, english.Variant);
        Assert.Equal(PrintLabels.English, english.Labels);
        Assert.Equal(standard.InvoiceData.DocumentNumber, english.InvoiceData.DocumentNumber);
        Assert.Equal(standard.InvoiceData.TotalGross, english.InvoiceData.TotalGross);
    }

    [Fact]
    public async Task Api013_PrintEndpointRefusesDuplicateBeforeAcceptance()
    {
        using var harness = new ContractHarness();
        var invoice = MakeDraftInvoice(harness.TenantId, "FV/DUPLICATE");
        await harness.SaveInvoiceAsync(invoice);

        var result = await harness.ExecuteAsync(controller =>
            controller.GetPrint(harness.TenantId, invoice.Id.Value, nameof(PrintVariant.Duplicate)));

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var error = Assert.IsType<RuleCodeErrorResponse>(conflict.Value);
        Assert.Equal("IMM-003", error.Code);
    }

    [Fact]
    public async Task Api014_TenantIsolation()
    {
        using var harness = new ContractHarness();
        var foreignTenantId = await harness.AddForeignTenantAsync("user-2", "2222222222");
        var invoice = MakeDraftInvoice(foreignTenantId, "FV/FOREIGN");
        await harness.SaveInvoiceAsync(invoice);

        var result = await harness.ExecuteAsync(controller =>
            controller.Get(foreignTenantId, invoice.Id.Value));

        Assert.IsType<NotFoundResult>(result);
    }

    private static Invoice MakeDraftInvoice(Guid tenantId, string documentNumber, string buyerNip = "9876543210")
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            new TenantId(tenantId),
            DocumentKind.VatInvoice,
            new SellerSnapshot(new PartyName("Seller"), new Nip("1234567890")),
            new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Business, new Nip(buyerNip)),
            CurrencyCode.Pln,
            new DateTime(2026, 4, 10),
            KsefSubmissionRequirement.Required,
            new DocumentNumber(documentNumber));

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, CurrencyCode.Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }

    private static Invoice MakeApprovedInvoice(Guid tenantId, string documentNumber)
    {
        var invoice = MakeDraftInvoice(tenantId, documentNumber);
        invoice.Approve(new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc));
        return invoice;
    }

    private static Invoice MakeAcceptedInvoice(Guid tenantId, string documentNumber)
    {
        var invoice = MakeApprovedInvoice(tenantId, documentNumber);
        invoice.SubmitToKsef(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));
        invoice.AcceptByKsef(
            new KsefIdentifiers("KSEF-123", "REF-123"),
            new DateTime(2026, 4, 10, 11, 0, 0, DateTimeKind.Utc));
        return invoice;
    }

    private static Invoice MakeAdvanceInvoice(Guid tenantId, string documentNumber, string buyerNip)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            new TenantId(tenantId),
            DocumentKind.AdvanceInvoice,
            new SellerSnapshot(new PartyName("Seller"), new Nip("1234567890")),
            new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Business, new Nip(buyerNip)),
            CurrencyCode.Pln,
            new DateTime(2026, 4, 10),
            KsefSubmissionRequirement.Required,
            new DocumentNumber(documentNumber));

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Advance",
            1m,
            new Money(50m, CurrencyCode.Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();
        return invoice;
    }

    private sealed class ContractHarness : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;

        public ContractHarness()
        {
            TenantId = Guid.NewGuid();
            CurrentUser = Substitute.For<ICurrentUserService>();
            CurrentUser.UserId.Returns("user-1");
            Clock = Substitute.For<IClock>();
            Clock.UtcNow.Returns(new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc));

            var services = new ServiceCollection();
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
            services.AddInvoiceApplication();
            services.AddInvoiceInfrastructure();
            services.AddSingleton<IInvoiceValidationSpecificationCatalog, InvoiceValidationSpecificationCatalog>();
            services.AddScoped<ICurrentUserService>(_ => CurrentUser);
            services.AddScoped<IClock>(_ => Clock);
            services.AddScoped<InvoiceValidationExceptionFilter>();
            services.AddScoped<InvoiceAggregateValidationContextFactory>();
            services.AddScoped<InvoiceAggregateMutationService>();

            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateScope();
            Db = _scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Repository = _scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
            Filter = _scope.ServiceProvider.GetRequiredService<InvoiceValidationExceptionFilter>();

            Db.Tenants.Add(new Tenant
            {
                Id = TenantId,
                UserId = "user-1",
                Nip = "1234567890",
                DisplayName = "Owned tenant",
                CreatedAt = Clock.UtcNow,
                UpdatedAt = Clock.UtcNow
            });
            Db.SaveChanges();
        }

        public Guid TenantId { get; }
        public ApplicationDbContext Db { get; }
        public IInvoiceRepository Repository { get; }
        public InvoiceValidationExceptionFilter Filter { get; }
        public ICurrentUserService CurrentUser { get; }
        public IClock Clock { get; }

        public async Task SaveInvoiceAsync(Invoice invoice) => await Repository.SaveAsync(invoice);

        public async Task AddKsefCredentialAsync()
        {
            Db.KSeFCredentials.Add(new KSeFCredential
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                EncryptedToken = "encrypted-token",
                CreatedAt = Clock.UtcNow,
                UpdatedAt = Clock.UtcNow
            });
            await Db.SaveChangesAsync();
        }

        public async Task<Guid> AddForeignTenantAsync(string userId, string nip)
        {
            var tenantId = Guid.NewGuid();
            Db.Tenants.Add(new Tenant
            {
                Id = tenantId,
                UserId = userId,
                Nip = nip,
                DisplayName = "Foreign tenant",
                CreatedAt = Clock.UtcNow,
                UpdatedAt = Clock.UtcNow
            });
            await Db.SaveChangesAsync();
            return tenantId;
        }

        public async Task<IActionResult> ExecuteAsync(
            Func<InvoicesAggregateController, Task<IActionResult>> action,
            ICreateInvoiceHandler? createHandler = null,
            IUpdateInvoiceDraftHandler? updateDraftHandler = null,
            IApproveInvoiceHandler? approveHandler = null,
            IReopenInvoiceHandler? reopenHandler = null,
            ICreateCorrectionFromOriginalHandler? createCorrectionHandler = null,
            ICreateFinalInvoiceFromAdvancesHandler? createFinalHandler = null,
            IRecordKsefAcceptanceHandler? recordAcceptanceHandler = null,
            IRecordKsefRejectionHandler? recordRejectionHandler = null,
            IInvoiceToKsefPayloadMapper? invoiceToKsefPayloadMapper = null)
        {
            var controller = new InvoicesAggregateController(
                Db,
                CurrentUser,
                createHandler ?? _scope.ServiceProvider.GetRequiredService<ICreateInvoiceHandler>(),
                updateDraftHandler ?? _scope.ServiceProvider.GetRequiredService<IUpdateInvoiceDraftHandler>(),
                approveHandler ?? _scope.ServiceProvider.GetRequiredService<IApproveInvoiceHandler>(),
                reopenHandler ?? _scope.ServiceProvider.GetRequiredService<IReopenInvoiceHandler>(),
                createCorrectionHandler ?? _scope.ServiceProvider.GetRequiredService<ICreateCorrectionFromOriginalHandler>(),
                createFinalHandler ?? _scope.ServiceProvider.GetRequiredService<ICreateFinalInvoiceFromAdvancesHandler>(),
                recordAcceptanceHandler ?? _scope.ServiceProvider.GetRequiredService<IRecordKsefAcceptanceHandler>(),
                recordRejectionHandler ?? _scope.ServiceProvider.GetRequiredService<IRecordKsefRejectionHandler>(),
                _scope.ServiceProvider.GetRequiredService<IInvoiceReadModelProjector<InvoiceReadDto>>(),
                _scope.ServiceProvider.GetRequiredService<Func<PrintVariant, InvoicePrintModelProjector>>(),
                Repository,
                _scope.ServiceProvider.GetRequiredService<DraftValidationService>(),
                _scope.ServiceProvider.GetRequiredService<KsefSubmissionValidationService>(),
                _scope.ServiceProvider.GetRequiredService<InvoiceAggregateValidationContextFactory>(),
                _scope.ServiceProvider.GetRequiredService<InvoiceAggregateMutationService>(),
                invoiceToKsefPayloadMapper ?? _scope.ServiceProvider.GetRequiredService<IInvoiceToKsefPayloadMapper>(),
                Clock);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = _scope.ServiceProvider
                }
            };

            try
            {
                return await action(controller);
            }
            catch (Exception exception)
            {
                var context = new ExceptionContext(
                    new ActionContext(
                        controller.HttpContext,
                        new RouteData(),
                        new ControllerActionDescriptor()),
                    new List<IFilterMetadata>())
                {
                    Exception = exception
                };

                await Filter.OnExceptionAsync(context);
                if (context.ExceptionHandled && context.Result is not null)
                {
                    return context.Result;
                }

                throw;
            }
        }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }

    private sealed class ThrowingApproveInvoiceHandler(ValidationResult validationResult) : IApproveInvoiceHandler
    {
        public ValidationResult Handle(Invoice invoice, ApproveInvoiceCommand command, ValidationContext context) =>
            throw new InvoiceDomainException(
                "Invoice approval blocked by validation.",
                stage: context.Stage,
                validationResult: validationResult);
    }

    private sealed class NeverAllowReopenPolicy : IApprovedEditPolicy
    {
        public bool CanReopen(Invoice invoice) => false;
    }
}
