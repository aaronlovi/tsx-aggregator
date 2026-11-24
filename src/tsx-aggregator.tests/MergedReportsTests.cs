using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dbm_persistence;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using tsx_aggregator.models;
using tsx_aggregator.Services;
using tsx_aggregator.shared;
using tsx_ops;
using tsx_ops.Commands;
using static tsx_aggregator.shared.Constants;

namespace tsx_aggregator.tests;

/// <summary>Review R31: tsx-ops merged-reports through AdminService.</summary>
public class MergedReportsTests {
    private static readonly DateOnly Mar = new(2022, 3, 31);
    private static readonly DateOnly Dec = new(2022, 12, 31);

    private static long _nextId = 1000;

    private static string Json(DateOnly date, string fields, bool currency = false) =>
        $"{{{fields},\"REPORTDATE\":\"{date:yyyy-MM-dd}T00:00:00Z\"{(currency ? ",\"CURRENCY\":\"CAD\"" : "")}}}";

    private static MergedReportCandidateDto Row(string symbol, DateOnly date, string json,
        ReportTypes type = ReportTypes.CashFlow, ReportPeriodTypes period = ReportPeriodTypes.Annual, bool alreadyReset = false) =>
        new(Interlocked.Increment(ref _nextId), symbol.GetHashCode(), symbol, symbol, (int)type, (int)period, date, json, alreadyReset);

    #region Planner

    private static IEnumerable<(long, DateOnly, MergedReportFinding, bool)> Findings(params MergedReportCandidateDto[] rows) =>
        MergedReportPlanner.Plan(rows, null).Items.Select(i => (i.Report.InstrumentReportId, i.OtherReportDate, i.Finding, i.Reset));

    [Fact]
    public void Plan_RowHoldingTheLaterReport_StoredUnderItsOwnDateToo_IsReset() {
        MergedReportCandidateDto a = Row("CF", Mar, Json(Dec, "\"A\":1,\"B\":2"));
        MergedReportCandidateDto b = Row("CF", Dec, Json(Dec, "\"A\":1"));

        Findings(b, a).Should().Equal(
            (a.InstrumentReportId, Dec, MergedReportFinding.WrongDate, true),
            (b.InstrumentReportId, Mar, MergedReportFinding.NotSent, false));
    }

    [Fact]
    public void Plan_RowHoldingTheOnlyCopyOfTheLaterReport_IsLeft() {
        // ATX: the 2025-12-31 report exists only inside the 2025-09-30 row until the company is scraped again.
        MergedReportCandidateDto a = Row("ATX", Mar, Json(Dec, "\"A\":1"));

        Findings(a).Should().Equal((a.InstrumentReportId, Dec, MergedReportFinding.OnlyCopy, false));
    }

    [Theory]
    [InlineData(ReportTypes.BalanceSheet, ReportPeriodTypes.Annual)]
    [InlineData(ReportTypes.CashFlow, ReportPeriodTypes.Quarterly)]
    public void Plan_TheSameDateInAnotherKindOfReport_IsNotItsOwnCopy(ReportTypes type, ReportPeriodTypes period) {
        MergedReportCandidateDto a = Row("ATX", Mar, Json(Dec, "\"A\":1"));
        MergedReportCandidateDto other = Row("ATX", Dec, Json(Dec, "\"A\":1"), type, period);

        Findings(a, other).Should().Equal((a.InstrumentReportId, Dec, MergedReportFinding.OnlyCopy, false));
    }

    [Fact]
    public void Plan_TwoRowsHoldingEachOthersDates_ResetsNeither() {
        // A row that is itself wrong is not a copy of the report of its date.
        MergedReportCandidateDto a = Row("X", Mar, Json(Dec, "\"A\":1"));
        MergedReportCandidateDto b = Row("X", Dec, Json(Mar, "\"A\":2"));

        MergedReportPlanner.Plan([a, b], null).Items.Should().HaveCount(2).And.OnlyContain(i => i.Finding == MergedReportFinding.OnlyCopy && !i.Reset);
    }

    [Fact]
    public void Plan_BothRowsOfAPair_SentByTmx_AreReset() {
        // Either row can hold leftovers: the old matching took whichever the database returned first (live: CF's earlier
        // rows, IFA's and MPC's later ones). Which fields they share says little: TMX adds fields, so a merged row need
        // not hold every field of the other one (live: ATD, ATX). Both can be fetched again.
        MergedReportCandidateDto a = Row("MPC", Mar, Json(Mar, "\"A\":1", currency: true));
        MergedReportCandidateDto b = Row("MPC", Dec, Json(Dec, "\"A\":9,\"B\":2", currency: true));

        Findings(b, a).Should().Equal(
            (a.InstrumentReportId, Dec, MergedReportFinding.LeftoverFields, true),
            (b.InstrumentReportId, Mar, MergedReportFinding.LeftoverFields, true));
    }

    [Fact]
    public void Plan_RowOfAPair_NotSentByTmx_IsLeft() {
        MergedReportCandidateDto a = Row("MDP", Mar, Json(Mar, "\"A\":1,\"B\":2"));
        MergedReportCandidateDto b = Row("MDP", Dec, Json(Dec, "\"A\":9", currency: true));

        Findings(a, b).Should().Equal(
            (a.InstrumentReportId, Dec, MergedReportFinding.NotSent, false),
            (b.InstrumentReportId, Mar, MergedReportFinding.LeftoverFields, true));
    }

    [Fact]
    public void Plan_LaterRowHoldingTheEarlierReport_IsReset_AndTheEarlierOneToo() {
        // IFA on live: the 2023-12-31 row holds the 2023-09-30 report, and may have been merged into that one before.
        MergedReportCandidateDto a = Row("IFA", Mar, Json(Mar, "\"A\":1", currency: true));
        MergedReportCandidateDto b = Row("IFA", Dec, Json(Mar, "\"A\":1,\"B\":2"));

        Findings(a, b).Should().Equal(
            (a.InstrumentReportId, Dec, MergedReportFinding.LeftoverFields, true),
            (b.InstrumentReportId, Mar, MergedReportFinding.WrongDate, true));
    }

    [Fact]
    public void Plan_TheOtherRow_IsTheNearestInTime() {
        MergedReportCandidateDto a = Row("IMG", new(2025, 4, 30), Json(new(2025, 4, 30), "\"A\":1", currency: true));
        MergedReportCandidateDto b = Row("IMG", new(2025, 6, 30), Json(new(2025, 6, 30), "\"A\":1", currency: true));
        MergedReportCandidateDto c = Row("IMG", new(2025, 12, 31), Json(new(2025, 12, 31), "\"A\":1", currency: true));

        MergedReportPlanner.Plan([c, a, b], null).Items.Select(i => i.OtherReportDate)
            .Should().Equal(new DateOnly(2025, 6, 30), new DateOnly(2025, 4, 30), new DateOnly(2025, 6, 30));
    }

    [Fact]
    public void Plan_ReportResetBefore_IsNotResetAgain() {
        Findings(
            Row("MDP", Mar, Json(Mar, "\"A\":1,\"B\":2", currency: true), alreadyReset: true),
            Row("MDP", Dec, Json(Dec, "\"A\":9", currency: true), alreadyReset: true)).Should().BeEmpty();
    }

    [Fact]
    public void Plan_ReportResetBefore_IsLeftOutAlsoWhenTmxNoLongerSendsIt() {
        MergedReportCandidateDto b = Row("MDP", Dec, Json(Dec, "\"A\":9", currency: true));

        Findings(Row("MDP", Mar, Json(Mar, "\"A\":1"), alreadyReset: true), b)
            .Should().Equal((b.InstrumentReportId, Mar, MergedReportFinding.LeftoverFields, true));
    }

    [Fact]
    public void Plan_DifferentYears_AreNotCompared() {
        Findings(
            Row("X", Mar.AddYears(-1), Json(Mar.AddYears(-1), "\"A\":1,\"B\":2", currency: true)),
            Row("X", Dec, Json(Dec, "\"A\":9", currency: true))).Should().BeEmpty();
    }

    [Fact]
    public void Plan_QuarterlyReports_AreComparedWithinTheQuarter() {
        MergedReportCandidateDto jun = Row("ATD", new(2025, 6, 30), Json(new(2025, 6, 30), "\"A\":1,\"B\":2", currency: true), period: ReportPeriodTypes.Quarterly);
        MergedReportCandidateDto jul = Row("ATD", new(2025, 7, 31), Json(new(2025, 7, 31), "\"A\":1,\"B\":2", currency: true), period: ReportPeriodTypes.Quarterly);
        MergedReportCandidateDto sep = Row("ATD", new(2025, 9, 30), Json(new(2025, 9, 30), "\"A\":3", currency: true), period: ReportPeriodTypes.Quarterly);

        Findings(sep, jun, jul).Should().Equal(
            (jul.InstrumentReportId, new DateOnly(2025, 9, 30), MergedReportFinding.LeftoverFields, true),
            (sep.InstrumentReportId, new DateOnly(2025, 7, 31), MergedReportFinding.LeftoverFields, true));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"A\":1,\"B\":2}")]                       // no report date
    [InlineData("{\"A\":1,\"B\":2,\"REPORTDATE\":\"soon\"}")]
    [InlineData("[1]")]
    public void Plan_RowThatCannotBeRead_IsLeftAlone(string json) {
        Findings(Row("X", Mar, json), Row("X", Dec, Json(Dec, "\"A\":9", currency: true))).Should().BeEmpty();
    }

    [Fact]
    public void Plan_Symbols_SelectCaseInsensitivelyAndReportTheRest() {
        MergedReportCandidateDto cf = Row("CF", Mar, Json(Dec, "\"A\":1"));
        MergedReportCandidateDto atx = Row("ATX", Mar, Json(Dec, "\"A\":1"));

        MergedReportPlan plan = MergedReportPlanner.Plan([cf, atx], ["atx", "ATX", "RY"]);

        plan.Items.Select(i => i.Report.InstrumentSymbol).Should().Equal("ATX");
        plan.NotFoundSymbols.Should().Equal("RY");
    }

    [Fact]
    public void Plan_IsOrderedBySymbolTypeAndDate() {
        MergedReportCandidateDto b2 = Row("B", Mar, Json(Dec, "\"A\":1"), ReportTypes.BalanceSheet);
        MergedReportCandidateDto b1 = Row("B", Mar, Json(Dec, "\"A\":1"), ReportTypes.CashFlow);
        MergedReportCandidateDto a = Row("A", Mar, Json(Dec, "\"A\":1"));

        MergedReportPlanner.Plan([b2, b1, a], null).Items.Select(i => i.Report).Should().Equal(a, b1, b2);
    }

    #endregion

    #region AdminService

    private sealed class Waker : IEventQueueWaker {
        public int Calls { get; private set; }
        public void CheckForEventsNow() => Calls++;
    }

    private readonly Mock<IDbmService> _dbm = new(MockBehavior.Strict);
    private readonly Waker _waker = new();
    private List<long>? _reset;

    private static readonly MergedReportCandidateDto CfWrong = Row("CF", Mar, Json(Dec, "\"A\":1,\"B\":2"));
    private static readonly MergedReportCandidateDto CfLater = Row("CF", Dec, Json(Dec, "\"A\":1"));
    private static readonly MergedReportCandidateDto AtxOnly = Row("ATX", Mar, Json(Dec, "\"A\":1"));

    private AdminSvc CreateService(bool aggregatorRuns = true, Result? resetResult = null) {
        _ = _dbm.Setup(d => d.GetMergedReportCandidates(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Result.SUCCESS, (IReadOnlyList<MergedReportCandidateDto>)[CfWrong, CfLater, AtxOnly]));
        _ = _dbm.Setup(d => d.ResetRawReports(It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<long> ids, CancellationToken _) => {
                _reset = [.. ids];
                return resetResult ?? Result.SUCCESS;
            });
        return new AdminSvc([], Options.Create(new HostedServicesOptions { RunAggregator = aggregatorRuns }),
            _dbm.Object, NullLogger<AdminSvc>.Instance, _waker);
    }

    [Fact]
    public async Task Repair_DryRun_ReportsWithoutResetting() {
        RepairMergedReportsReply reply = await CreateService().RepairMergedReports(new(), new TestServerCallContext());

        reply.Success.Should().BeTrue();
        reply.Executed.Should().BeFalse();
        reply.Reports.Should().HaveCount(3);
        reply.Reports[0].Should().BeEquivalentTo(new MergedReport {
            CompanySymbol = "ATX", InstrumentSymbol = "ATX", InstrumentReportId = AtxOnly.InstrumentReportId, ReportType = 1,
            ReportPeriodType = 1, ReportDate = "2022-03-31", OtherReportDate = "2022-12-31", Finding = MergedReportFinding.OnlyCopy, Reset = false,
        });
        reply.Reports[1].Finding.Should().Be(MergedReportFinding.WrongDate);
        reply.Reports[1].Reset.Should().BeTrue();
        reply.Reports[2].Finding.Should().Be(MergedReportFinding.NotSent);
        reply.Reports[2].Reset.Should().BeFalse();
        _reset.Should().BeNull();
        _waker.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Repair_Execute_ResetsOnlyTheRepairableAndWakesTheAggregator() {
        RepairMergedReportsReply reply = await CreateService().RepairMergedReports(new() { Execute = true }, new TestServerCallContext());

        reply.Success.Should().BeTrue();
        reply.Executed.Should().BeTrue();
        _reset.Should().Equal(CfWrong.InstrumentReportId);
        reply.AggregatorWoken.Should().BeTrue();
        _waker.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Repair_Execute_AggregatorNotRunning_ResetsWithoutWaking() {
        RepairMergedReportsReply reply = await CreateService(aggregatorRuns: false).RepairMergedReports(new() { Execute = true }, new TestServerCallContext());

        reply.Executed.Should().BeTrue();
        reply.AggregatorWoken.Should().BeFalse();
        _waker.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Repair_Execute_Symbols_ResetOnlyThose() {
        var request = new RepairMergedReportsRequest { Execute = true };
        request.InstrumentSymbols.Add("ATX");

        RepairMergedReportsReply reply = await CreateService().RepairMergedReports(request, new TestServerCallContext());

        reply.Executed.Should().BeFalse(); // nothing to reset
        reply.Reports.Select(r => r.InstrumentSymbol).Should().Equal("ATX");
        _reset.Should().BeNull();
    }

    [Fact]
    public async Task Repair_Execute_UnknownSymbol_ResetsNothing() {
        var request = new RepairMergedReportsRequest { Execute = true };
        request.InstrumentSymbols.AddRange(["CF", "TYPO"]);

        RepairMergedReportsReply reply = await CreateService().RepairMergedReports(request, new TestServerCallContext());

        reply.Success.Should().BeFalse();
        reply.NotFoundSymbols.Should().Equal("TYPO");
        _reset.Should().BeNull();
    }

    [Fact]
    public async Task Repair_DatabaseError_IsReported() {
        RepairMergedReportsReply reply = await CreateService(resetResult: Result.SetFailure("db down"))
            .RepairMergedReports(new() { Execute = true }, new TestServerCallContext());

        reply.Success.Should().BeFalse();
        reply.Executed.Should().BeFalse();
        reply.ErrorMessage.Should().Contain("db down");
        _waker.Calls.Should().Be(0);
    }

    #endregion

    #region tsx-ops command

    private readonly Mock<AdminService.AdminServiceClient> _client = new();
    private readonly StringWriter _output = new();
    private RepairMergedReportsRequest? _sent;

    private static AsyncUnaryCall<T> Call<T>(T reply) =>
        new(Task.FromResult(reply), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });

    private Task<int> RunCommand(RepairMergedReportsReply reply, params string[] args) {
        _ = _client.Setup(c => c.RepairMergedReportsAsync(It.IsAny<RepairMergedReportsRequest>(), null, null, It.IsAny<CancellationToken>()))
            .Callback((RepairMergedReportsRequest r, Metadata _, DateTime? _, CancellationToken _) => _sent = r)
            .Returns(Call(reply));
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ops:AggregatorGrpcUrl"] = "http://ops.test:7001" })
            .Build();
        using OpsContext context = OpsContext.CreateForTests(config, _output);
        return new MergedReportsCommand(_ => _client.Object).RunAsync(args, context, CancellationToken.None);
    }

    private static MergedReport Report(string company, string instrument, long id, MergedReportFinding finding, bool reset) => new() {
        CompanySymbol = company, InstrumentSymbol = instrument, InstrumentReportId = id, ReportType = 1, ReportPeriodType = 1,
        ReportDate = "2022-03-31", OtherReportDate = "2022-12-31", Finding = finding, Reset = reset,
    };

    private static RepairMergedReportsReply SampleReply(bool executed) {
        var reply = new RepairMergedReportsReply { Success = true, Executed = executed, AggregatorWoken = executed };
        reply.Reports.AddRange([
            Report("ATX", "ATX", 7, MergedReportFinding.OnlyCopy, reset: false),
            Report("CF", "CF", 8, MergedReportFinding.WrongDate, reset: true),
            Report("MPC", "MPC", 9, MergedReportFinding.LeftoverFields, reset: true),
            Report("MPC", "MPC.C", 10, MergedReportFinding.LeftoverFields, reset: true),
        ]);
        return reply;
    }

    [Fact]
    public async Task Command_WithoutExecute_SendsADryRunAndListsTheRows() {
        int exit = await RunCommand(SampleReply(executed: false));

        exit.Should().Be(ExitCodes.Ok);
        _sent!.Execute.Should().BeFalse();
        _sent.InstrumentSymbols.Should().BeEmpty();
        string output = _output.ToString();
        output.Should().Contain("Would reset 3 of 4").And.Contain("--execute").And.Contain("ATX").And.Contain("cash flow")
            .And.Contain("annual").And.Contain("2022-03-31").And.Contain("2022-12-31");
        output.Should().NotContain("priority set"); // only after the reports are gone
    }

    [Fact]
    public async Task Command_Execute_ListsTheIdsAndTheCompaniesToScrapeNext() {
        int exit = await RunCommand(SampleReply(executed: true), "--execute");

        exit.Should().Be(ExitCodes.Ok);
        _sent!.Execute.Should().BeTrue();
        _output.ToString().Should().Contain("Reset 3 of 4").And.Contain("8, 9, 10").And.Contain("priority set CF,MPC --execute")
            // The priority queue scrapes one instrument per company (Registry.FindFirstInstrumentByCompanySymbol).
            .And.Contain("more than one listed instrument here: MPC");
    }

    [Fact]
    public async Task Command_Symbols_AreSent() {
        _ = await RunCommand(new RepairMergedReportsReply { Success = true }, "cf", "MPC");

        _sent!.InstrumentSymbols.Should().Equal("cf", "MPC");
        _output.ToString().Should().Contain("No merged reports found");
    }

    [Fact]
    public async Task Command_FailedReply_IsAnError() {
        int exit = await RunCommand(new RepairMergedReportsReply { Success = false, ErrorMessage = "db down" }, "--execute");

        exit.Should().Be(ExitCodes.Error);
        _output.ToString().Should().Contain("db down");
    }

    [Fact]
    public void Command_IsRegistered() => tsx_ops.Program.Commands.Should().ContainSingle(c => c.Name == "merged-reports");

    #endregion

    private sealed class TestServerCallContext : ServerCallContext {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => [];
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(null, []);
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
