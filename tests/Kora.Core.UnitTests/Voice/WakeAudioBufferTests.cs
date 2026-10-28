using AwesomeAssertions;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Voice;

public sealed class WakeAudioBufferTests
{
    [Fact]
    public void Retirement_prevents_already_queued_callbacks_from_repopulating_cleared_memory()
    {
        var buffer = new WakeAudioBuffer(4);
        buffer.Append([1, 2]);
        buffer.Retire();
        buffer.Append([3, 4]);
        buffer.Append([5], knownPlayback: true);
        buffer.Retire();
        buffer.Count.Should().Be(0);
        buffer.EndSample.Should().Be(2);
        var consume = () => buffer.TakeCommand(2);
        consume.Should().Throw<InvalidOperationException>();
    }
    [Fact]
    public void Native_binding_denies_capture_until_the_host_supplies_a_live_gate()
    {
        var binding = new HandsFreeAudioBinding(-2, new("synthetic", "Synthetic"), "Nova",
            new(1, SpeechInputSelection.Default));
        binding.CanCapture().Should().BeFalse();
        binding.CommandPhrases.Should().BeEmpty();
        var admitted = binding with { CanCapture = static () => true, CommandPhrases = ["session help"] };
        admitted.CanCapture().Should().BeTrue();
        admitted.CommandPhrases.Should().Equal("session help");
    }
    [Fact]
    public void Two_second_ring_preserves_no_pause_words_but_not_preceding_or_wake_audio()
    {
        var buffer = new WakeAudioBuffer(4);
        buffer.Capacity.Should().Be(8);
        buffer.Append([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        buffer.Count.Should().Be(8);
        buffer.EndSample.Should().Be(10);
        buffer.TakeCommand(7).Should().Equal(8, 9, 10);
        buffer.Count.Should().Be(0);
        var replay = () => buffer.TakeCommand(7);
        replay.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(11)]
    public void Stale_future_or_missing_boundary_clears_every_sample(long boundary)
    {
        var buffer = new WakeAudioBuffer(4);
        buffer.Append([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        var take = () => buffer.TakeCommand(boundary);
        take.Should().Throw<InvalidOperationException>();
        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void Playback_and_explicit_clear_never_reappear_in_a_later_command()
    {
        var buffer = new WakeAudioBuffer(4);
        buffer.Append([1, 2, 3]);
        buffer.Append([4, 5], knownPlayback: true);
        buffer.Count.Should().Be(0);
        buffer.EndSample.Should().Be(5);
        buffer.Append([6, 7]);
        buffer.TakeCommand(5).Should().Equal(6, 7);
        buffer.Append([8, 9]);
        buffer.Clear();
        buffer.Count.Should().Be(0);
        buffer.Append([10]);
        buffer.TakeCommand(buffer.EndSample).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(192001)]
    public void Invalid_audio_format_is_not_reinterpreted_as_a_default(int rate)
    {
        var create = () => new WakeAudioBuffer(rate);
        create.Should().Throw<ArgumentOutOfRangeException>();
    }
}
