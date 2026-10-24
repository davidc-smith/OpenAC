using AcDream.Core.Physics;
using AcDream.Runtime.Gameplay;

namespace AcDream.Runtime.Tests.Gameplay;

/// <summary>
/// Between the moment the character's body is given to the runtime and the
/// moment it goes live in the world, the body is not walking anywhere. The
/// walk check that runs every frame must say so instead of reaching into a
/// body that may not be touched yet. A client whose collision arrives over
/// several frames holds the body in that state across frames.
/// </summary>
public sealed class RuntimeApproachDormantBodyTests
{
    [Fact]
    public void A_body_not_yet_live_is_walking_nowhere_and_the_frame_goes_on()
    {
        using GameRuntime runtime = NewRuntime();
        PlayerMovementController dormant =
            PlayerMovementController.CreatePublicationCandidate(
                new PhysicsEngine(),
                PlayerMovementConstructionOptions.Fallback);
        dormant.SealPublicationCandidate();
        dormant.CommitRuntimeOwnership(new RetailObjectQuantumClock());
        runtime.MovementOwner.Controller = dormant;
        Assert.False(dormant.CanExecuteLiveMovement);

        Exception? failure = Record.Exception(
            runtime.ArmedApproachDrive.DriveArmedApproaches);

        Assert.Null(failure);
    }

    [Fact]
    public void The_remote_body_pass_finds_no_walk_on_a_body_not_yet_live()
    {
        using GameRuntime runtime = NewRuntime();
        runtime.MovementOwner.Controller = NewDormant();

        Exception? failure = Record.Exception(runtime.FinishRemoteBodyPass);

        Assert.Null(failure);
    }

    private static PlayerMovementController NewDormant()
    {
        PlayerMovementController dormant =
            PlayerMovementController.CreatePublicationCandidate(
                new PhysicsEngine(),
                PlayerMovementConstructionOptions.Fallback);
        dormant.SealPublicationCandidate();
        dormant.CommitRuntimeOwnership(new RetailObjectQuantumClock());
        Assert.False(dormant.CanExecuteLiveMovement);
        return dormant;
    }

    private static GameRuntime NewRuntime()
    {
        var gameplay = new RuntimeCreatureDeathStateTests.NoOpOperations();
        return new GameRuntime(new GameRuntimeDependencies(
            gameplay,
            gameplay,
            gameplay,
            gameplay));
    }
}
