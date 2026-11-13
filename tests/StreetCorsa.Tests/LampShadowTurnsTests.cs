using StreetCorsa.Controls;

namespace StreetCorsa.Tests;

public class LampShadowTurnsTests
{
    [Fact]
    public void A_still_scene_draws_every_stale_lamp_at_once_and_a_moving_one_a_quarter_of_them_a_frame()
    {
        Assert.Equal(0, LampShadowBudget.For(casting: 8, stale: 0, busy: false));
        Assert.Equal(8, LampShadowBudget.For(casting: 8, stale: 8, busy: false));
        Assert.Equal(2, LampShadowBudget.For(casting: 8, stale: 8, busy: true));
        Assert.Equal(3, LampShadowBudget.For(casting: 9, stale: 9, busy: true));
        Assert.Equal(1, LampShadowBudget.For(casting: 4, stale: 4, busy: true));
        Assert.Equal(1, LampShadowBudget.For(casting: 8, stale: 1, busy: true));
    }
}
