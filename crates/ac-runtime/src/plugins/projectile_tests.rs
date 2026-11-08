//! Projectile paths. Cases from OpenAC
//! `tests/AcDream.HostParity.Tests/Scenarios/ProjectilePathParityTests.cs`
//! (the flight through the session's collision world: a creature in the
//! open is clear, one under the ground is blocked by it, an unknown id is
//! an invalid target) and `RuntimeAutomationSurface.EvaluateProjectile
//! PathRequest`'s guards. The flights need the retail dats: set
//! `OPENAC_DAT_DIR` or install them under `Documents\Asheron's Call`; run
//! with `cargo test -- --include-ignored`.

use ac_physics::content::publish_landblock;
use ac_physics::{CollisionWorld, PhysicsEngine};
use ac_plugin::abi::calls;
use ac_plugin::abi::combat::AttackHeight;
use ac_plugin::abi::projectile::{ProjectilePathKind, ProjectilePathQuery, ProjectilePathStatus};
use glam::Vec3;

use super::projectile::{Flight, evaluate};
use super::tests::{call, in_world, with_binding};
use crate::session::Outbox;

/// OpenAC's parity query: 0.4 m probe, 0.7 m steps, 500 checks.
fn query(target_id: u32, kind: ProjectilePathKind) -> ProjectilePathQuery {
    ProjectilePathQuery {
        target_id,
        kind,
        target_height: AttackHeight::Medium,
        projectile_radius: 0.4,
        step_distance: 0.7,
        maximum_collision_checks: 500,
        diagnostics: false,
        launch_speed: None,
    }
}

// OpenAC `EvaluateProjectilePathRequest`: a zero id, a radius or step that
// is not a positive finite number, or no budget is an invalid target; an
// object the client does not know is too; with no character placed there
// is nothing to shoot from.
#[test]
fn unusable_queries_and_unknown_targets_are_invalid() {
    let mut state = in_world();
    let mut outbox = Outbox::new();
    with_binding(&mut state, &mut outbox, |b| {
        assert!(call::<calls::ProjectilesAvailable>(b, &()));
        let ask = |b: &mut _, q: ProjectilePathQuery| call::<calls::ProjectilesEvaluatePath>(b, &q);
        let invalid = |q: ProjectilePathQuery| q;
        for q in [
            query(0, ProjectilePathKind::Straight),
            ProjectilePathQuery {
                projectile_radius: 0.0,
                ..query(0x5000_FFFF, ProjectilePathKind::Straight)
            },
            ProjectilePathQuery {
                step_distance: f32::NAN,
                ..query(0x5000_FFFF, ProjectilePathKind::Straight)
            },
            ProjectilePathQuery {
                maximum_collision_checks: 0,
                ..query(0x5000_FFFF, ProjectilePathKind::Straight)
            },
            query(0x5000_FFFF, ProjectilePathKind::Straight),
        ] {
            assert_eq!(
                ask(b, invalid(q)).status,
                ProjectilePathStatus::InvalidTarget
            );
        }
    });
    // Out of the world: unavailable.
    let mut state = in_world();
    let mut outbox = Outbox::new();
    with_binding(&mut state, &mut outbox, |b| {
        b.facts.in_world = false;
        assert!(!call::<calls::ProjectilesAvailable>(b, &()));
        assert_eq!(
            call::<calls::ProjectilesEvaluatePath>(b, &query(1, ProjectilePathKind::Straight))
                .status,
            ProjectilePathStatus::Unavailable
        );
    });
}

fn holtburg() -> PhysicsEngine {
    let dir = std::env::var_os("OPENAC_DAT_DIR").map(std::path::PathBuf::from);
    let content = crate::movement::WorldContent::open(dir.as_deref()).expect("the retail dats");
    let mut engine = PhysicsEngine::new();
    engine.attach_world(CollisionWorld::new());
    publish_landblock(
        &mut engine,
        content.dats(),
        content.height_table(),
        0xA9B4_FFFF,
        Vec3::ZERO,
    )
    .unwrap();
    engine
}

/// Flat open ground in Holtburg (the movement tests' spot).
const SPOT: (f32, f32) = (60.0, 150.0);

fn outdoor_cell(x: f32, y: f32) -> u32 {
    0xA9B4_0000 | (1 + (x / 24.0) as u32 * 8 + (y / 24.0) as u32)
}

#[test]
#[ignore = "needs the retail dat files"]
fn a_shot_in_the_open_is_clear_and_one_under_the_ground_is_blocked() {
    let engine = holtburg();
    let (x, y) = SPOT;
    let ground = engine.sample_terrain_z(x, y).unwrap();
    let shooter = Vec3::new(x, y, ground);
    let flight = |target: Vec3| Flight {
        shooter_id: 1,
        shooter,
        cell_id: outdoor_cell(x, y),
        target_id: 2,
        target,
    };
    let near = flight(Vec3::new(
        x + 12.0,
        y,
        engine.sample_terrain_z(x + 12.0, y).unwrap(),
    ));
    for kind in [
        ProjectilePathKind::Straight,
        ProjectilePathKind::Arc,
        ProjectilePathKind::Missile,
    ] {
        let answer = evaluate(&engine, &near, &query(2, kind), None);
        assert_ne!(answer.status, ProjectilePathStatus::Unavailable, "{kind:?}");
        assert!(answer.collision_checks > 0, "{kind:?}");
    }
    let straight = evaluate(
        &engine,
        &near,
        &query(2, ProjectilePathKind::Straight),
        None,
    );
    assert_eq!(straight.status, ProjectilePathStatus::Clear);
    // 12 m in 0.7 m steps: the flight ends on its last step.
    assert!(
        (15..=20).contains(&straight.collision_checks),
        "{straight:?}"
    );
    // The ground is in the way of a creature 6 m under it.
    let buried = flight(Vec3::new(x + 12.0, y, ground - 6.0));
    let blocked = evaluate(
        &engine,
        &buried,
        &query(2, ProjectilePathKind::Straight),
        None,
    );
    assert_eq!(blocked.status, ProjectilePathStatus::Blocked, "{blocked:?}");
    assert_eq!(blocked.blocking_object_id, 0, "the landscape stopped it");
    // A budget smaller than the flight runs out; diagnostics keep a marker
    // per step.
    let mut samples = Vec::new();
    let short = evaluate(
        &engine,
        &near,
        &ProjectilePathQuery {
            maximum_collision_checks: 3,
            ..query(2, ProjectilePathKind::Straight)
        },
        Some(&mut samples),
    );
    assert_eq!(short.status, ProjectilePathStatus::BudgetExceeded);
    assert_eq!(short.collision_checks, 3);
    assert_eq!(samples.len(), 3);
    assert!(samples.iter().all(|(_, clear)| *clear));
}

// ── A launch speed (API 1.10): a missile or arc flies at that total speed
// on the lower arc that reaches the aim point, as a server launches one.

fn launched_at(speed: f32, target_id: u32, kind: ProjectilePathKind) -> ProjectilePathQuery {
    ProjectilePathQuery {
        launch_speed: Some(speed),
        ..query(target_id, kind)
    }
}

/// A flight over nothing: the shot starts 1.2 m up and aims 0.9 m up, so
/// the aim point is `across` metres out (less the 0.61 m a missile starts
/// ahead) and 0.3 m lower.
fn over_nothing(across: f32) -> Flight {
    Flight {
        shooter_id: 1,
        shooter: Vec3::ZERO,
        cell_id: 0,
        target_id: 2,
        target: Vec3::new(across, 0.0, 0.0),
    }
}

// A target no arc reaches at the launch speed is an invalid target, before
// any collision check: at 20 m/s the farthest reach on level ground is
// 20² / 9.8, about 40.8 m. A speed that is not above 0 is unusable.
#[test]
fn a_target_out_of_reach_at_the_launch_speed_is_invalid() {
    let engine = PhysicsEngine::new();
    for kind in [ProjectilePathKind::Missile, ProjectilePathKind::Arc] {
        let far = evaluate(
            &engine,
            &over_nothing(45.0),
            &launched_at(20.0, 2, kind),
            None,
        );
        assert_eq!(far.status, ProjectilePathStatus::InvalidTarget, "{kind:?}");
        assert_eq!(far.collision_checks, 0);
        assert_eq!(
            far.notice.as_deref(),
            Some("The target is out of reach at 20.0 m/s."),
            "{kind:?}"
        );
        for unusable in [0.0, -5.0, f32::NAN] {
            let answer = evaluate(
                &engine,
                &over_nothing(10.0),
                &launched_at(unusable, 2, kind),
                None,
            );
            assert_eq!(answer.status, ProjectilePathStatus::InvalidTarget);
            assert_eq!(
                answer.notice.as_deref(),
                Some("The launch speed must be above 0 m/s.")
            );
        }
    }
    // At most 50 m/s: 300 m is beyond 50² / 9.8 (255 m) however fast the
    // query says.
    let clamped = evaluate(
        &engine,
        &over_nothing(300.0),
        &launched_at(400.0, 2, ProjectilePathKind::Missile),
        None,
    );
    assert_eq!(
        clamped.notice.as_deref(),
        Some("The target is out of reach at 50.0 m/s.")
    );
    // A straight shot has no launch speed: it is not out of reach, and
    // flies as before (here into a world with nothing loaded).
    let straight = evaluate(
        &engine,
        &over_nothing(45.0),
        &launched_at(20.0, 2, ProjectilePathKind::Straight),
        None,
    );
    let before = evaluate(
        &engine,
        &over_nothing(45.0),
        &query(2, ProjectilePathKind::Straight),
        None,
    );
    assert_eq!(straight, before);
    assert_ne!(straight.status, ProjectilePathStatus::InvalidTarget);
}

/// Something overhead, `id`: a sphere of `radius` whose lowest point is
/// `clearance` metres above the ground at (`x`, `y`).
fn overhead(engine: &mut PhysicsEngine, id: u32, x: f32, y: f32, clearance: f32, radius: f32) {
    use ac_physics::{
        EntityCollisionFlags, Quat, RegistrationSite, ShadowCollisionType, SingleShape,
    };
    let ground = engine.sample_terrain_z(x, y).unwrap();
    let cell = outdoor_cell(x, y);
    let (shadows, world) = engine.shadows_and_world();
    shadows
        .register(
            world,
            id,
            &SingleShape {
                gfx_obj_id: 0,
                radius,
                collision_type: ShadowCollisionType::Sphere,
                cyl_height: 0.0,
                scale: 1.0,
            },
            RegistrationSite {
                world_pos: Vec3::new(x, y, ground + clearance + radius),
                rotation: Quat::IDENTITY,
                world_offset_x: 0.0,
                world_offset_y: 0.0,
                landblock_id: (cell & 0xFFFF_0000) | 0xFFFF,
                seed_cell_id: cell,
            },
            0,
            EntityCollisionFlags(0),
            false,
            None,
        )
        .expect("register");
}

// A launcher's arrow at 20 m/s climbs about 5 m on its way to a creature
// 35 m out, where the probe at 46 m/s across stays under 2 m: with
// something 3.2 m over the middle of the flight, the slower arc hits it and
// the default one passes under.
#[test]
#[ignore = "needs the retail dat files"]
fn a_slower_launch_lobs_into_a_low_ceiling_the_default_probe_passes_under() {
    const CEILING: u32 = 0x8000_7101;
    let mut engine = holtburg();
    let (x, y) = SPOT;
    let ground = engine.sample_terrain_z(x, y).unwrap();
    let across = 35.0;
    let target = Vec3::new(
        x + across,
        y,
        engine.sample_terrain_z(x + across, y).unwrap(),
    );
    let flight = Flight {
        shooter_id: 1,
        shooter: Vec3::new(x, y, ground),
        cell_id: outdoor_cell(x, y),
        target_id: 2,
        target,
    };
    // Nothing overhead yet: both clear, and the launch is in the notice.
    let default = evaluate(
        &engine,
        &flight,
        &query(2, ProjectilePathKind::Missile),
        None,
    );
    assert_eq!(default.status, ProjectilePathStatus::Clear, "{default:?}");
    assert_eq!(default.notice, None);
    let slow = evaluate(
        &engine,
        &flight,
        &launched_at(20.0, 2, ProjectilePathKind::Missile),
        None,
    );
    assert_eq!(slow.status, ProjectilePathStatus::Clear, "{slow:?}");
    let notice = slow.notice.as_deref().unwrap();
    assert!(notice.starts_with("Launched at 20.0 m/s, "), "{notice}");
    // The low arc to a point 34.4 m out and 0.3 m lower at 20 m/s leaves
    // about 28 degrees up.
    let degrees: f32 = notice["Launched at 20.0 m/s, ".len()..]
        .trim_end_matches("° up.")
        .parse()
        .unwrap();
    assert!((27.0..30.0).contains(&degrees), "{notice}");
    // A slower flight takes more steps: 0.7 m of its longer, slower path
    // each.
    assert!(slow.collision_checks > default.collision_checks);

    overhead(&mut engine, CEILING, x + across / 2.0, y, 3.2, 6.0);
    let default = evaluate(
        &engine,
        &flight,
        &query(2, ProjectilePathKind::Missile),
        None,
    );
    assert_eq!(default.status, ProjectilePathStatus::Clear, "{default:?}");
    let mut samples = Vec::new();
    let slow = evaluate(
        &engine,
        &flight,
        &launched_at(20.0, 2, ProjectilePathKind::Missile),
        Some(&mut samples),
    );
    assert_eq!(slow.status, ProjectilePathStatus::Blocked, "{slow:?}");
    assert_eq!(slow.blocking_object_id, CEILING);
    assert!(
        slow.notice
            .as_deref()
            .unwrap()
            .starts_with("Launched at 20.0 m/s, ")
    );
    // It went up past the ceiling's lowest point before it hit.
    let highest = samples.iter().map(|(at, _)| at.z).fold(f32::MIN, f32::max);
    assert!(highest > ground + 3.2, "{highest} over {ground}");
    // Fast enough, the arc stays under it again.
    let fast = evaluate(
        &engine,
        &flight,
        &launched_at(45.0, 2, ProjectilePathKind::Missile),
        None,
    );
    assert_eq!(fast.status, ProjectilePathStatus::Clear, "{fast:?}");
}
