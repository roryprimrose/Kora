using AwesomeAssertions;

using Kora.Application.Infrastructure;

namespace Kora.Application.UnitTests.Infrastructure;

public sealed class ObservableObjectTests
{
    [Fact]
    public void SetProperty_changes_value_and_notifies_only_for_a_new_value()
    {
        var subject = new TestObservable();
        var properties = new List<string?>();
        subject.PropertyChanged += (_, eventArgs) => properties.Add(eventArgs.PropertyName);

        subject.Value = "new";
        subject.Value = "new";

        subject.Value.Should().Be("new");
        properties.Should().Equal(nameof(TestObservable.Value));
    }

    [Fact]
    public void Property_changes_work_without_subscribers()
    {
        var subject = new TestObservable();

        subject.Value = "new";
        subject.NotifyOther();

        subject.Value.Should().Be("new");
    }

    [Fact]
    public void OnPropertyChanged_notifies_the_caller_member()
    {
        var subject = new TestObservable();
        string? property = null;
        subject.PropertyChanged += (_, eventArgs) => property = eventArgs.PropertyName;

        subject.NotifyOther();

        property.Should().Be(nameof(TestObservable.NotifyOther));
    }

    private sealed class TestObservable : ObservableObject
    {
        private string value = "initial";

        public string Value
        {
            get => value;
            set => SetProperty(ref this.value, value);
        }

        public void NotifyOther() => OnPropertyChanged();
    }
}