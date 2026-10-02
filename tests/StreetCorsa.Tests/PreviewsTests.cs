using System.Numerics;
using StreetCorsa.Controls;
using StreetCorsa.Models.GameState;
using StreetCorsa.Parts;
using StreetCorsa.Parts.Logic;
using StreetCorsa.Screens.CarViewing;
using StreetCorsa.Screens.Shared;
using StreetCorsa.Services.Market;
using StreetCorsa.Services.Opponents;
using StreetCorsa.Services.Street;

namespace StreetCorsa.Tests;

/// <summary>Step 25's 3D previews: how a part and a car are framed, where a car is gone to see, and what a purchase leads to</summary>
public class PreviewsTests
{
    private const float FovY = 32f * MathF.PI / 180f;

    #region Framing a part

    [Fact]
    public void A_part_stands_on_the_floor_in_the_middle()
    {
        var min = new Vector3(-0.2f, 0.35f, 1.1f);
        var max = new Vector3(0.4f, 0.95f, 1.5f);

        var offset = PreviewFraming.PartOffset(min, max);

        Assert.Equal(0f, min.Y + offset.Y, 5);
        Assert.Equal(0f, (min.X + max.X) / 2f + offset.X, 5);
        Assert.Equal(0f, (min.Z + max.Z) / 2f + offset.Z, 5);
        Assert.Equal(new Vector3(0f, 0.3f, 0f), PreviewFraming.PartTarget(min, max));
    }

    [Theory]
    [InlineData(0.9f, 0.8f, 1.0f, 1.7f)]  // an engine, in the dialog's wide picture
    [InlineData(0.12f, 0.12f, 0.03f, 1.7f)] // a pulley
    [InlineData(0.65f, 0.65f, 0.25f, 1f)]  // a wheel, in a square picture
    [InlineData(2f, 0.1f, 0.1f, 1.7f)]     // a long thin rod
    public void A_part_is_framed_whole_whichever_way_it_turns(float x, float y, float z, float aspect)
    {
        const float beta = 0.36f;
        var size = new Vector3(x, y, z);
        var distance = PreviewFraming.PartDistance(size, FovY, aspect, beta);
        var halfY = MathF.Tan(FovY / 2f);
        var halfX = halfY * aspect;

        // Every corner of the box, turned round the middle, stays in the picture
        for (var turn = 0; turn < 24; turn++)
        {
            var alpha = turn * MathF.PI / 12f;
            var eye = new Vector3(MathF.Cos(beta) * MathF.Cos(alpha), MathF.Sin(beta), MathF.Cos(beta) * MathF.Sin(alpha)) * distance;
            var view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
            foreach (var corner in Corners(size / 2f))
            {
                var p = Vector3.Transform(corner, view);
                var depth = -p.Z;
                Assert.True(depth > 0f);
                Assert.InRange(Math.Abs(p.X / depth), 0f, halfX);
                Assert.InRange(Math.Abs(p.Y / depth), 0f, halfY);
            }
        }
    }

    [Fact]
    public void A_part_fills_a_fair_share_of_the_picture()
    {
        // An engine is not lost in the middle of the picture: its height takes a good part of it
        var size = new Vector3(0.9f, 0.8f, 1.0f);
        var distance = PreviewFraming.PartDistance(size, FovY, 1.7f, 0.36f);
        var share = size.Y / 2f / (distance * MathF.Tan(FovY / 2f));

        Assert.InRange(share, 0.3f, 1f);
    }

    [Fact]
    public void A_bigger_part_is_seen_from_further_and_a_tiny_one_is_not_blown_up()
    {
        var engine = PreviewFraming.PartDistance(new Vector3(0.9f, 0.8f, 1f), FovY, 1.7f, 0.36f);
        var carb = PreviewFraming.PartDistance(new Vector3(0.25f, 0.2f, 0.25f), FovY, 1.7f, 0.36f);
        var washer = PreviewFraming.PartDistance(new Vector3(0.01f, 0.002f, 0.01f), FovY, 1.7f, 0.36f);
        var screw = PreviewFraming.PartDistance(new Vector3(0.004f, 0.004f, 0.004f), FovY, 1.7f, 0.36f);

        Assert.True(engine > carb);
        Assert.True(carb > washer);
        Assert.Equal(washer, screw, 4);
        Assert.True(PreviewFraming.NearPlane(PreviewFraming.BoundingRadius(Vector3.Zero, new Vector3(0.01f))) < washer / 2f);
    }

    [Fact]
    public void The_near_plane_stays_in_front_of_the_part_and_sensible()
    {
        Assert.Equal(0.005f, PreviewFraming.NearPlane(0.01f));
        Assert.Equal(0.1f, PreviewFraming.NearPlane(50f));
        Assert.True(PreviewFraming.NearPlane(0.5f) < 0.5f);
    }

    private static IEnumerable<Vector3> Corners(Vector3 half)
    {
        foreach (var sx in new[] { -1f, 1f })
        foreach (var sy in new[] { -1f, 1f })
        foreach (var sz in new[] { -1f, 1f })
            yield return new Vector3(half.X * sx, half.Y * sy, half.Z * sz);
    }

    #endregion

    #region Framing a car

    /// <summary>Where the orbit camera stands, as AcTools has it</summary>
    private static Vector2 Ground(float alpha) => new(MathF.Cos(alpha), MathF.Sin(alpha));

    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(200f)]
    [InlineData(-35f)]
    public void The_car_is_seen_from_its_left_and_its_nose_as_asked(float heading)
    {
        var h = heading * MathF.PI / 180f;
        var left = new Vector2(MathF.Cos(h), -MathF.Sin(h));
        var nose = new Vector2(MathF.Sin(h), MathF.Cos(h));

        AssertClose(left, Ground(PreviewFraming.CarCameraAlpha(heading, 0f)));
        AssertClose(nose, Ground(PreviewFraming.CarCameraAlpha(heading, MathF.PI / 2f)));

        // The front three-quarter is between the two
        var threeQuarter = Ground(PreviewFraming.CarCameraAlpha(heading, 0.8f));
        Assert.True(Vector2.Dot(threeQuarter, left) > 0.5f);
        Assert.True(Vector2.Dot(threeQuarter, nose) > 0.5f);
    }

    [Fact]
    public void A_longer_car_is_seen_from_further_and_whole()
    {
        var coupe = PreviewFraming.CarDistance(4.2f, FovY, 16f / 9f);
        var wagon = PreviewFraming.CarDistance(5.6f, FovY, 16f / 9f);

        Assert.True(wagon > coupe);
        Assert.True(5.6f / 2f < wagon * MathF.Tan(FovY / 2f) * 16f / 9f);
    }

    private static void AssertClose(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
    }

    #endregion

    #region Where the car is gone to see

    [Fact]
    public void A_dealers_car_is_seen_on_the_dealers_lot()
    {
        var car = new UsedCarListingViewModel { Listing = new UsedCarListing { CarDefinitionId = "car", DealerLocation = "downtown_motors" } };

        var seller = CarViewingPlace.Of(car, id => id == "downtown_motors");

        Assert.Equal(CarViewingSpot.DealerLot, seller.Spot);
        Assert.Equal("downtown_motors", seller.DealerId);
        Assert.Null(seller.Rival);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a_dealer_no_mod_has")]
    public void Anyone_elses_car_is_seen_at_the_curb(string dealer)
    {
        var car = new UsedCarListingViewModel { Listing = new UsedCarListing { CarDefinitionId = "car", DealerLocation = dealer } };

        var seller = CarViewingPlace.Of(car, id => id == "downtown_motors");

        Assert.Equal(CarViewingSpot.Curb, seller.Spot);
        Assert.Null(seller.DealerId);
        Assert.Null(seller.Rival);
    }

    [Fact]
    public void A_rivals_car_is_seen_at_the_curb_and_the_ad_is_what_is_bought()
    {
        var state = GameState.CreateNew("P");
        var driven = new Car("car_a");
        var spare = new Car("car_b");
        var pat = new Opponent("Pat", 30, Gender.Female, 92, 50) { Status = RacerStatus.ReadyToRace };
        pat.Cars.AddRange([driven, spare]);
        state.Racers.AddRacer(pat);
        var ad = RivalCarAds.Post(state, pat, spare, 900m, state.Date);

        // The paper's ad carries the rival's own ad, which is what is bought
        var car = new UsedCarListingViewModel
        {
            Listing = new UsedCarListing { Id = ad.AdId.ToString(), CarDefinitionId = "car_b", DealerLocation = string.Empty },
            RivalAd = ad,
            PrivateSeller = pat.Name
        };
        var seller = CarViewingPlace.Of(car, _ => true);

        Assert.Equal(CarViewingSpot.Curb, seller.Spot);
        Assert.Null(seller.DealerId);
        Assert.Same(ad, seller.Rival);
    }

    [Theory]
    [InlineData(PurchaseOutcome.Bought, AfterPurchase.Garage)]
    [InlineData(PurchaseOutcome.NoLongerAvailable, AfterPurchase.Newspaper)]
    [InlineData(PurchaseOutcome.NotEnoughMoney, AfterPurchase.Stay)]
    public void After_a_purchase_the_player_goes_home_back_to_the_paper_or_stays(PurchaseOutcome outcome, AfterPurchase expected)
    {
        Assert.Equal(expected, CarViewingPlace.Next(new PurchaseResult(outcome, "")));
    }

    private static StreetScene Street(string id, params StreetLight[] times) =>
        new(id, id, $"C:\\streets\\{id}", new StreetSpot(-1.5f, 0f, 0f), 3.3f, 0.35f, 45f, 70f, [0.2f, 1f, 0.8f],
            new Dictionary<StreetLight, StreetLighting>())
        {
            Times = new HashSet<StreetLight>(times)
        };

    [Fact]
    public void A_private_seller_lives_on_a_street_used_at_that_hour_and_the_same_one_every_visit()
    {
        var streets = new[] { Street("night_only", StreetLight.Night), Street("a", StreetLight.Day, StreetLight.Dusk), Street("b", StreetLight.Day) };

        for (var i = 0; i < 20; i++)
        {
            var id = $"listing-{i}";
            var first = CarViewingPlace.StreetFor(streets, StreetLight.Day, id);
            var again = CarViewingPlace.StreetFor(streets, StreetLight.Day, id);

            Assert.NotNull(first);
            Assert.NotEqual("night_only", first.Value.Street.Id);
            Assert.Equal(StreetLight.Day, first.Value.Light);
            Assert.Same(first.Value.Street, again!.Value.Street);
        }
    }

    [Fact]
    public void A_street_used_at_no_such_hour_is_seen_in_its_nearest_light_and_none_is_none()
    {
        var found = CarViewingPlace.StreetFor([Street("day_only", StreetLight.Day)], StreetLight.Night, "x");

        Assert.Equal("day_only", found?.Street.Id);
        Assert.Equal(StreetLight.Day, found?.Light);
        Assert.Null(CarViewingPlace.StreetFor([], StreetLight.Day, "x"));
    }

    #endregion

    #region The part's model

    [Fact]
    public void A_part_the_catalog_does_not_have_has_no_picture()
    {
        using var temp = new TempDir();
        var catalog = PartsCatalog.Empty(temp.Path);

        Assert.False(PartPreviewModel.HasModel(catalog, new PartInstance("nobody/has_this")));
        Assert.Null(PartPreviewModel.Build(catalog, new PartInstance("nobody/has_this")));
    }

    [Fact]
    public void A_merged_model_is_moved_without_being_built_again()
    {
        using var temp = new TempDir();
        var catalog = PartsCatalog.Empty(temp.Path);
        var part = new PartDefinition { Id = "x/box", Name = "box" };
        var model = PartAssembler.BuildModel(catalog, "test",
            [new PlacedPart(part, Matrix4x4.CreateTranslation(1f, 2f, 3f), new InstalledPart(part))]);

        var moved = PartAssembler.Moved(model, new Vector3(-1f, -2f, 0f));

        Assert.Same(model.Kn5, moved.Kn5);
        Assert.Equal(model.Min + new Vector3(-1f, -2f, 0f), moved.Min);
        Assert.Equal(model.Max + new Vector3(-1f, -2f, 0f), moved.Max);
        Assert.Equal(new Vector3(0f, 0f, 3f), moved.Nodes[0].World.Translation);
    }

    #endregion
}
