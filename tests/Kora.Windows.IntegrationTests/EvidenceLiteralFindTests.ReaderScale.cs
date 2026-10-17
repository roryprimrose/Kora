using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.NativeUxFixture;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class EvidenceLiteralFindTests
{
    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public async Task ConfirmedScaleChangesOnlyCompleteSerializedBodyAndFocusedSpanFromOriginalTypography(int percent)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var listener = WindowsDailyEvidenceReaderTests.Listen();
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            var reader = new Reader();
            var viewer = Viewer(reader);
            var window = new EvidenceWindow(viewer, fixture.Main);
            window.Show();
            try
            {
                var body = window.FindControl<NamedTextBlock>("EvidenceBody")!;
                var match = window.FindControl<NamedTextBlock>("LiteralMatch")!;
                var bodyFont = body.FontSize;
                var matchFont = match.FontSize;
                // Exercise the shared helper's explicit line-height path without modifying content.
                var geometry = new TextBlock { FontSize = 16, LineHeight = 24 };
                var apply = NativeReaderTypography.Capture(geometry);
                var chrome = window.GetVisualDescendants().OfType<TextBlock>()
                    .Where(control => !ReferenceEquals(control, body) && !ReferenceEquals(control, match))
                    .ToDictionary(control => control, control => (control.FontSize, control.LineHeight, AutomationProperties.GetName(control)));
                var fields = window.GetVisualDescendants().OfType<TextBox>().ToDictionary(control => control, control => control.FontSize);
                var width = window.Width;
                var height = window.Height;
                await viewer.SearchAsync();
                var text = viewer.ResultText;
                var records = viewer.Records;
                viewer.Select(records[0]);
                window.FindControl<TextBox>("LiteralQuery")!.Text = "aaa";
                Click(window, "FindFirst");
                var span = viewer.FindSelection;
                var revision = viewer.FindRevision;
                var status = viewer.FindStatus;
                var names = (AutomationProperties.GetName(body), AutomationProperties.GetName(match));
                var heights = new Dictionary<int, double>();
                foreach (var scale in new[] { percent, 200, 75, 100, percent })
                {
                    fixture.Main.ReaderTextScalePercent = scale;
                    await fixture.Main.AppearanceUpdate;
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    body.FontSize.Should().Be(bodyFont * scale / 100d);
                    match.FontSize.Should().Be(matchFont * scale / 100d);
                    apply(scale);
                    geometry.FontSize.Should().Be(16 * scale / 100d);
                    geometry.LineHeight.Should().Be(24 * scale / 100d);
                    match.Bounds.Height.Should().BeGreaterThan(0);
                    heights[scale] = match.Bounds.Height;
                    match.IsFocused.Should().BeTrue();
                    foreach (var (control, original) in chrome)
                    {
                        (control.FontSize, control.LineHeight, AutomationProperties.GetName(control)).Should().Be(original);
                    }
                    foreach (var (control, original) in fields) { control.FontSize.Should().Be(original); }
                    window.Width.Should().Be(width);
                    window.Height.Should().Be(height);
                    viewer.ResultText.Should().BeSameAs(text);
                    body.Text.Should().Be(text);
                    viewer.Records.Should().BeSameAs(records);
                    viewer.CanReadTrace.Should().BeTrue();
                    viewer.CanNext.Should().BeTrue();
                    viewer.FindSelection.Should().Be(span);
                    viewer.FindRevision.Should().Be(revision);
                    viewer.FindStatus.Should().Be(status);
                    match.Text.Should().Be("aaa");
                    (AutomationProperties.GetName(body), AutomationProperties.GetName(match)).Should().Be(names);
                    reader.Queries.Should().ContainSingle();
                }
                heights[200].Should().BeGreaterThan(heights[75]);
                var secondViewer = Viewer(new Reader());
                var second = new EvidenceWindow(secondViewer, fixture.Main);
                second.Show();
                try
                {
                    await secondViewer.SearchAsync();
                    second.FindControl<NamedTextBlock>("EvidenceBody")!.FontSize.Should().Be(bodyFont * percent / 100d);
                }
                finally { second.Close(); }
                window.Close();
                fixture.Main.ReaderTextScalePercent = percent == 75 ? 200 : 75;
                await fixture.Main.AppearanceUpdate;
                Dispatcher.UIThread.RunJobs();
                body.Text.Should().BeEmpty();
                match.Text.Should().BeEmpty();
                body.FontSize.Should().Be(bodyFont);
                match.FontSize.Should().Be(matchFont);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrivateClosureOrLatePreferenceRevocationRetiresTheReaderWithoutAdditionalReads(bool latePreference)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var listener = WindowsDailyEvidenceReaderTests.Listen();
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            fixture.Main.ReaderTextScalePercent = 200;
            await fixture.Main.AppearanceUpdate;
            Dispatcher.UIThread.RunJobs();
            var reader = new Reader();
            var access = new Access();
            var viewer = Viewer(reader, access);
            var window = new EvidenceWindow(viewer, fixture.Main);
            window.Show();
            var body = window.FindControl<NamedTextBlock>("EvidenceBody")!;
            var match = window.FindControl<NamedTextBlock>("LiteralMatch")!;
            var baseline = body.FontSize;
            try
            {
                await viewer.SearchAsync();
                Match(viewer);
                var revision = viewer.FindRevision;
                if (latePreference)
                {
                    var held = new TaskCompletionSource<EvidenceReadBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
                    reader.Read = () => new(held.Task);
                    var pending = viewer.SearchAsync();
                    File.WriteAllText(Path.Combine(fixture.LocalRoot, "Preferences", "reader-text-scale-unconfirmed.txt"), "1");
                    held.SetResult(reader.Batch());
                    await pending;
                    window.IsVisible.Should().BeFalse();
                }
                else
                {
                    access.CanInspect = false;
                    fixture.Main.ReaderTextScalePercent = 75;
                    await fixture.Main.AppearanceUpdate;
                    Dispatcher.UIThread.RunJobs();
                }
                viewer.IsFindCurrent(revision).Should().BeFalse();
                Retired(viewer);
                viewer.CanFind.Should().BeFalse();
                body.Text.Should().BeEmpty();
                match.Text.Should().BeEmpty();
                body.FontSize.Should().Be(baseline);
                match.FontSize.Should().Be(baseline);
                reader.Queries.Should().HaveCount(latePreference ? 2 : 1);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("replace")]
    [InlineData("source")]
    [InlineData("private")]
    [InlineData("close")]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("partial")]
    [InlineData("omitted")]
    public async Task RetirementAndLateReadsNeverRestoreScaledMatchOrRetiredContent(string change)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var listener = WindowsDailyEvidenceReaderTests.Listen();
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            fixture.Main.ReaderTextScalePercent = 200;
            await fixture.Main.AppearanceUpdate;
            var reader = new Reader();
            var access = new Access();
            var viewer = Viewer(reader, access);
            var window = new EvidenceWindow(viewer, fixture.Main);
            window.Show();
            var body = window.FindControl<NamedTextBlock>("EvidenceBody")!;
            var match = window.FindControl<NamedTextBlock>("LiteralMatch")!;
            var baseline = body.FontSize;
            try
            {
                await viewer.SearchAsync();
                Match(viewer);
                body.FontSize.Should().Be(baseline * 2);
                var revision = viewer.FindRevision;
                var held = new TaskCompletionSource<EvidenceReadBatch>(TaskCreationOptions.RunContinuationsAsynchronously);
                reader.Read = () => new(held.Task);
                var pending = viewer.SearchAsync();
                body.Text.Should().BeEmpty();
                body.FontSize.Should().Be(baseline);
                match.Text.Should().BeEmpty();
                match.FontSize.Should().Be(baseline);
                switch (change)
                {
                    case "source": viewer.Source = EvidenceSource.DailyLog; break;
                    case "private": access.CanInspect = false; viewer.EnsureFindAdmission(); break;
                    case "close": window.Close(); break;
                    case "failure": held.SetException(new InvalidDataException("synthetic failure")); break;
                    case "cancel": held.SetException(new OperationCanceledException()); break;
                }
                if (!held.Task.IsCompleted)
                {
                    held.SetResult(change switch
                    {
                        "partial" => reader.Batch() with { Status = EvidencePageStatus.Partial },
                        "omitted" => reader.Batch() with { Candidates = [new(new(1, EvidenceSource.Log, "one", 0), reader.Record with { ContentOmitted = true })] },
                        _ => reader.Batch(),
                    });
                }
                await pending;
                viewer.IsFindCurrent(revision).Should().BeFalse();
                Retired(viewer);
                match.Text.Should().BeEmpty();
                match.IsVisible.Should().BeFalse();
                match.FontSize.Should().Be(baseline);
                body.FontSize.Should().Be(string.Equals(change, "replace", StringComparison.Ordinal) ? baseline * 2 : baseline);
                if (change is not ("replace" or "partial" or "omitted")) { body.Text.Should().BeEmpty(); }
                reader.Queries.Should().HaveCount(2);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("74")]
    [InlineData("201")]
    [InlineData("100\n")]
    [InlineData("unconfirmed")]
    [InlineData("external")]
    public async Task InvalidPreferenceEvidenceExplicitlyRetiresOpenReaderAndDeniesNewReader(string state)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var listener = WindowsDailyEvidenceReaderTests.Listen();
            using var fixture = new NativeUxFixtureSession(Path.GetTempPath());
            await fixture.InitializeAsync();
            fixture.Main.ReaderTextScalePercent = 200;
            await fixture.Main.AppearanceUpdate;
            Dispatcher.UIThread.RunJobs();
            var reader = new Reader();
            var viewer = Viewer(reader);
            var window = new EvidenceWindow(viewer, fixture.Main);
            window.Show();
            var body = window.FindControl<NamedTextBlock>("EvidenceBody")!;
            var match = window.FindControl<NamedTextBlock>("LiteralMatch")!;
            var baseline = body.FontSize;
            try
            {
                await viewer.SearchAsync();
                Match(viewer);
                body.FontSize.Should().Be(baseline * 2);
                var file = string.Equals(state, "unconfirmed", StringComparison.Ordinal)
                    ? "reader-text-scale-unconfirmed.txt" : "reader-text-scale-percent.txt";
                File.WriteAllText(Path.Combine(fixture.LocalRoot, "Preferences", file),
                    state switch { "unconfirmed" => "1", "external" => "75", _ => state });
                fixture.Main.ReaderTextScalePercent = 75;
                await fixture.Main.AppearanceUpdate;
                Dispatcher.UIThread.RunJobs();
                window.IsVisible.Should().BeFalse();
                viewer.CanQuery.Should().BeFalse();
                Retired(viewer);
                body.Text.Should().BeEmpty();
                match.Text.Should().BeEmpty();
                body.FontSize.Should().Be(baseline);
                match.FontSize.Should().Be(baseline);
                fixture.Main.ResponseBody.Should().Contain("preference evidence");
                var nextViewer = Viewer(reader);
                try
                {
                    var open = () => new EvidenceWindow(nextViewer, fixture.Main);
                    open.Should().Throw<InvalidOperationException>().WithMessage("*text scale is unavailable*");
                }
                finally { nextViewer.Close(); }
                reader.Queries.Should().ContainSingle();
            }
            finally { window.Close(); }
        });
    }
}
