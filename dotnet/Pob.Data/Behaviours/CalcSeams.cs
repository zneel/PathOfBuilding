using System.Diagnostics.CodeAnalysis;

namespace Pob.Data.Behaviours;

// The behaviour delegates have to name the types the Lua hooks are called with
// (`func(activeSkill, output, breakdown)`, src/Modules/CalcOffence.lua:509-514)
// before the calc engine exists. These are those names and nothing more: the
// members land in tickets 21-24, which own the calc types, and every behaviour
// body written in ticket 19 will be typed against them from the start, so the
// registry contract does not change shape when they fill in.
//
// CA1040 objects to empty interfaces because they usually carry no contract.
// Here the emptiness is the contract: an accidental member added now would be a
// guess at CalcOffence's shape made by the wrong ticket.

/// <summary>
/// The active skill instance a behaviour is invoked for.
/// Lua: <c>activeSkill</c> (see <c>src/Modules/CalcActiveSkill.lua</c>). Members arrive with ticket 22.
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces",
    Justification = "Seam for the calc types owned by tickets 21-24; deliberately has no members yet.")]
public interface IActiveSkill;

/// <summary>
/// The string-keyed output table a behaviour reads and writes.
/// Lua: <c>output</c> (see the <c>---@class Output</c> annotations in <c>src/Modules/CalcBase.lua</c>).
/// Members arrive with ticket 21.
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces",
    Justification = "Seam for the calc types owned by tickets 21-24; deliberately has no members yet.")]
public interface ISkillOutput;

/// <summary>
/// The optional breakdown sink. Lua passes <c>nil</c> when breakdowns are off, which is why every
/// hook parameter of this type is nullable. Members arrive with ticket 23.
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces",
    Justification = "Seam for the calc types owned by tickets 21-24; deliberately has no members yet.")]
public interface ISkillBreakdown;

/// <summary>
/// The calculation environment. Lua: <c>env</c>. Only <c>explosiveArrowFunc</c> takes one
/// (<c>src/Modules/CalcOffence.lua:3038</c>). Members arrive with ticket 20.
/// </summary>
[SuppressMessage("Design", "CA1040:Avoid empty interfaces",
    Justification = "Seam for the calc types owned by tickets 21-24; deliberately has no members yet.")]
public interface ICalcEnvironment;
