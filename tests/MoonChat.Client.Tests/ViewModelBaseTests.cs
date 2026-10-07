using System.ComponentModel;
using MoonChat.Client.ViewModels;

namespace MoonChat.Client.Tests;

/// <summary>
/// <see cref="ViewModelBase"/> underpins every view model in the app. Its
/// SetProperty contract (raise only on an actual change, return whether it
/// changed) is what stops the list redrawing on every keystroke, so it is worth
/// pinning directly. A tiny local subclass exposes the protected members.
/// </summary>
public class ViewModelBaseTests
{
    private sealed class Probe : ViewModelBase
    {
        private string _text = "";
        private int _count;

        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }

        public int Count
        {
            get => _count;
            set => SetProperty(ref _count, value);
        }

        public bool SetDirectly(string value) => SetProperty(ref _text, value, nameof(Explicit));

        public string Explicit => _text;

        public void Touch(string? name = null) => OnPropertyChanged(name);

        public void TouchMany(params string[] names) => OnPropertiesChanged(names);
    }

    private static (Probe Model, List<string?> Changes) Tracked()
    {
        var model = new Probe();
        var changes = new List<string?>();
        model.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        return (model, changes);
    }

    [Fact]
    public void SetProperty_raises_PropertyChanged_with_the_property_name()
    {
        var (model, changes) = Tracked();

        model.Text = "hello";

        Assert.Equal("hello", model.Text);
        Assert.Equal([nameof(Probe.Text)], changes);
    }

    [Fact]
    public void SetProperty_does_not_raise_when_the_value_is_unchanged()
    {
        var (model, changes) = Tracked();
        model.Text = "same";

        model.Text = "same";

        Assert.Equal([nameof(Probe.Text)], changes);
    }

    [Fact]
    public void SetProperty_raises_again_once_the_value_differs()
    {
        var (model, changes) = Tracked();
        model.Text = "a";
        model.Text = "a";
        model.Text = "b";

        Assert.Equal([nameof(Probe.Text), nameof(Probe.Text)], changes);
    }

    [Fact]
    public void SetProperty_is_case_sensitive()
    {
        var (model, changes) = Tracked();
        model.Text = "a";

        model.Text = "A";

        Assert.Equal(2, changes.Count);
        Assert.Equal("A", model.Text);
    }

    [Fact]
    public void SetProperty_accepts_null_for_a_nullable_reference()
    {
        var (model, _) = Tracked();

        model.Text = null!;

        Assert.Null(model.Text);
    }

    [Fact]
    public void SetProperty_compares_structs_by_value()
    {
        var (model, changes) = Tracked();

        model.Count = 5;
        model.Count = 5;

        Assert.Equal([nameof(Probe.Count)], changes);
    }

    [Fact]
    public void SetProperty_compares_zero_and_the_default_primitive()
    {
        var (model, changes) = Tracked();

        model.Count = 0;

        Assert.Empty(changes);
    }

    [Fact]
    public void SetProperty_honours_an_explicit_property_name()
    {
        var (model, changes) = Tracked();

        model.SetDirectly("value");

        Assert.Equal([nameof(Probe.Explicit)], changes);
    }

    [Fact]
    public void OnPropertyChanged_raises_the_requested_name()
    {
        var (model, changes) = Tracked();

        model.Touch("Custom");

        Assert.Equal(["Custom"], changes);
    }

    [Fact]
    public void OnPropertiesChanged_raises_every_name_in_order()
    {
        var (model, changes) = Tracked();

        model.TouchMany("A", "B", "C");

        Assert.Equal(["A", "B", "C"], changes);
    }

    [Fact]
    public void OnPropertiesChanged_with_no_names_raises_nothing()
    {
        var (model, changes) = Tracked();

        model.TouchMany();

        Assert.Empty(changes);
    }

    [Fact]
    public void unsubscribing_stops_notifications()
    {
        var model = new Probe();
        var count = 0;
        void Handler(object? _, PropertyChangedEventArgs __) => count++;

        model.PropertyChanged += Handler;
        model.Text = "a";
        model.PropertyChanged -= Handler;
        model.Text = "b";

        Assert.Equal(1, count);
    }

    [Fact]
    public void a_derived_view_model_gets_the_same_behaviour()
    {
        // LoginViewModel and friends derive from ViewModelBase; make sure the
        // base is genuinely usable without an Avalonia app running.
        var model = new Probe();

        model.Text = "x";

        Assert.Equal("x", model.Text);
        Assert.IsAssignableFrom<INotifyPropertyChanged>(model);
    }
}