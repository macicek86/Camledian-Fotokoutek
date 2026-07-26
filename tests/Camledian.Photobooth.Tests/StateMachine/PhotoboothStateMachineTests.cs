using Camledian.Photobooth.Core.StateMachine;

namespace Camledian.Photobooth.Tests.StateMachine;

public class PhotoboothStateMachineTests
{
    [Fact]
    public void StartsIdle()
    {
        var fsm = new PhotoboothStateMachine();
        Assert.Equal(PhotoboothState.Idle, fsm.Current);
    }

    [Theory]
    [InlineData(PhotoboothState.Idle, PhotoboothState.SelectingBackground, true)]
    [InlineData(PhotoboothState.Idle, PhotoboothState.Preview, false)]
    [InlineData(PhotoboothState.Idle, PhotoboothState.Capturing, false)]
    [InlineData(PhotoboothState.SelectingBackground, PhotoboothState.Preview, true)]
    [InlineData(PhotoboothState.Preview, PhotoboothState.Countdown, true)]
    [InlineData(PhotoboothState.Countdown, PhotoboothState.Capturing, true)]
    [InlineData(PhotoboothState.Countdown, PhotoboothState.Preview, true)]
    [InlineData(PhotoboothState.Capturing, PhotoboothState.Processing, true)]
    [InlineData(PhotoboothState.Capturing, PhotoboothState.SelectingPhoto, true)]
    [InlineData(PhotoboothState.Capturing, PhotoboothState.Idle, false)]
    [InlineData(PhotoboothState.SelectingPhoto, PhotoboothState.Processing, true)]
    [InlineData(PhotoboothState.SelectingPhoto, PhotoboothState.Preview, true)]
    [InlineData(PhotoboothState.SelectingPhoto, PhotoboothState.Idle, true)]
    [InlineData(PhotoboothState.SelectingPhoto, PhotoboothState.Result, false)]
    [InlineData(PhotoboothState.Processing, PhotoboothState.Result, true)]
    [InlineData(PhotoboothState.Result, PhotoboothState.Printing, true)]
    [InlineData(PhotoboothState.Result, PhotoboothState.SelectingBackground, true)]
    [InlineData(PhotoboothState.Printing, PhotoboothState.Result, true)]
    [InlineData(PhotoboothState.Admin, PhotoboothState.Idle, true)]
    [InlineData(PhotoboothState.Admin, PhotoboothState.Capturing, false)]
    public void OnlyDeclaredTransitionsAreAllowed(PhotoboothState from, PhotoboothState to, bool expected)
    {
        var fsm = new PhotoboothStateMachine(from);
        Assert.Equal(expected, fsm.CanFire(to));
    }

    [Fact]
    public void AnyStateCanTransitionToError()
    {
        foreach (var state in Enum.GetValues<PhotoboothState>())
        {
            var fsm = new PhotoboothStateMachine(state);
            Assert.True(fsm.CanFire(PhotoboothState.Error), $"{state} should be able to transition to Error");
        }
    }

    [Fact]
    public void TryFireRejectsIllegalTransitionAndKeepsCurrentState()
    {
        var fsm = new PhotoboothStateMachine();
        var fired = fsm.TryFire(PhotoboothState.Capturing);

        Assert.False(fired);
        Assert.Equal(PhotoboothState.Idle, fsm.Current);
    }

    [Fact]
    public void FireThrowsOnIllegalTransition()
    {
        var fsm = new PhotoboothStateMachine();
        Assert.Throws<InvalidOperationException>(() => fsm.Fire(PhotoboothState.Capturing));
    }

    [Fact]
    public void StateChangedRaisedWithFromAndTo()
    {
        var fsm = new PhotoboothStateMachine();
        StateChangedEventArgs? captured = null;
        fsm.StateChanged += (_, e) => captured = e;

        fsm.Fire(PhotoboothState.SelectingBackground);

        Assert.NotNull(captured);
        Assert.Equal(PhotoboothState.Idle, captured!.From);
        Assert.Equal(PhotoboothState.SelectingBackground, captured.To);
    }

    [Fact]
    public void ResetGoesToIdleFromAnyState()
    {
        var fsm = new PhotoboothStateMachine(PhotoboothState.Result);
        fsm.Reset();
        Assert.Equal(PhotoboothState.Idle, fsm.Current);
    }

    [Fact]
    public void ResetIsNoOpEventWhenAlreadyIdle()
    {
        var fsm = new PhotoboothStateMachine();
        var raised = false;
        fsm.StateChanged += (_, _) => raised = true;

        fsm.Reset();

        Assert.False(raised);
    }

    [Fact]
    public void FullHappyPathWorkflowSucceeds()
    {
        var fsm = new PhotoboothStateMachine();

        Assert.True(fsm.TryFire(PhotoboothState.SelectingBackground));
        Assert.True(fsm.TryFire(PhotoboothState.Preview));
        Assert.True(fsm.TryFire(PhotoboothState.Countdown));
        Assert.True(fsm.TryFire(PhotoboothState.Capturing));
        Assert.True(fsm.TryFire(PhotoboothState.Processing));
        Assert.True(fsm.TryFire(PhotoboothState.Result));
        Assert.True(fsm.TryFire(PhotoboothState.Printing));
        Assert.True(fsm.TryFire(PhotoboothState.Result));
        Assert.True(fsm.TryFire(PhotoboothState.Idle));

        Assert.Equal(PhotoboothState.Idle, fsm.Current);
    }

    [Fact]
    public void BurstWorkflowWithPhotoSelectionSucceeds()
    {
        var fsm = new PhotoboothStateMachine();

        Assert.True(fsm.TryFire(PhotoboothState.SelectingBackground));
        Assert.True(fsm.TryFire(PhotoboothState.Preview));
        Assert.True(fsm.TryFire(PhotoboothState.Countdown));
        Assert.True(fsm.TryFire(PhotoboothState.Capturing));
        Assert.True(fsm.TryFire(PhotoboothState.SelectingPhoto));
        Assert.True(fsm.TryFire(PhotoboothState.Processing));
        Assert.True(fsm.TryFire(PhotoboothState.Result));

        Assert.Equal(PhotoboothState.Result, fsm.Current);
    }

    [Fact]
    public void BurstRetakeFromSelectionGoesBackToPreview()
    {
        var fsm = new PhotoboothStateMachine(PhotoboothState.SelectingPhoto);
        Assert.True(fsm.TryFire(PhotoboothState.Preview));
        Assert.True(fsm.TryFire(PhotoboothState.Countdown));
    }
}
