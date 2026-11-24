// How much food is left on one dish, 0 to 100, and what shows it.
// Implemented by whatever actually holds the food - CouscousDishController for the couscous,
// CoffeeCup for the mug - and read and written by FoodDish, which owns the rules of eating but
// nothing about what the food looks like. That split is the point: a dish is anything that can say
// how full it is and show it, so adding a second one to the kitchen is a new implementation of this
// interface and no change at all to the match, the HUD or the networking.
// Percent rather than 0 to 1 because that is what both implementations already expose to the
// Inspector, and because the dish being drained is a thing an artist scrubs by hand.
public interface IDishFill
{
    float FillPercent { get; set; }
}
