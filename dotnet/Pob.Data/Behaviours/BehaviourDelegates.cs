namespace Pob.Data.Behaviours;

/// <summary>
/// A skill behaviour: the C# form of a Lua closure stored in a granted effect
/// (<c>initialFunc</c>, <c>preSkillTypeFunc</c>, <c>preDamageFunc</c>, <c>postCritFunc</c>).
/// All four are invoked through <c>runSkillFunc</c> with the same three arguments
/// (src/Modules/CalcOffence.lua:509-514), so they share one delegate type.
/// </summary>
/// <param name="activeSkill">The skill being calculated.</param>
/// <param name="output">The output table for this skill.</param>
/// <param name="breakdown">The breakdown sink, or <see langword="null"/> when breakdowns are off.</param>
public delegate void SkillBehaviour(IActiveSkill activeSkill, ISkillOutput output, ISkillBreakdown? breakdown);

/// <summary>
/// The one behaviour with its own signature: <c>explosiveArrowFunc</c>, called directly rather than
/// through <c>runSkillFunc</c> (src/Modules/CalcOffence.lua:3038).
/// </summary>
/// <param name="activeSkill">The skill being calculated.</param>
/// <param name="output">The output table for this skill.</param>
/// <param name="globalOutput">The output table of the calculation as a whole.</param>
/// <param name="globalBreakdown">The global breakdown sink, or <see langword="null"/>.</param>
/// <param name="environment">The calculation environment.</param>
public delegate void ExplosiveArrowBehaviour(
    IActiveSkill activeSkill,
    ISkillOutput output,
    ISkillOutput globalOutput,
    ISkillBreakdown? globalBreakdown,
    ICalcEnvironment environment);
