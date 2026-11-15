using FM27Access.Navigation;

namespace FM27Access.Tests;

public class MatchConditionsTests
{
    // The reading captured live from the dev driver on 2026-08-06 (a 6,833-seat ground, evening
    // kick-off, drizzle). Pins the whole line against real engine values rather than invented ones.
    [Fact]
    public void Compose_MatchesTheVerifiedLiveReading()
    {
        Assert.Equal("Attendance: 3,189, 47% of capacity. 341 away fans. 8 degrees, drizzle.",
            MatchConditions.Compose(3189, 6833, 341, 8f, "Drizzle"));
    }

    [Fact]
    public void Compose_ThousandsSeparatorsOnLargeCrowds()
    {
        Assert.Equal("Attendance: 74,310, 98% of capacity. 3,000 away fans. 14 degrees, dry.",
            MatchConditions.Compose(74310, 75800, 3000, 14f, "Dry"));
    }

    [Fact]
    public void Compose_NoAwayAllocationIsStatedNotOmitted()
    {
        Assert.Equal("Attendance: 3,189, 47% of capacity. No away fans. 8 degrees, drizzle.",
            MatchConditions.Compose(3189, 6833, 0, 8f, "Drizzle"));
    }

    [Fact]
    public void Compose_UnreadableAttendanceStillSpeaksTheRest()
    {
        Assert.Equal("attendance unavailable. 341 away fans. 8 degrees, drizzle.",
            MatchConditions.Compose(null, 6833, 341, 8f, "Drizzle"));
    }

    [Fact]
    public void Compose_UnreadableWeatherStillSpeaksTheRest()
    {
        Assert.Equal("Attendance: 3,189, 47% of capacity. 341 away fans. weather unavailable.",
            MatchConditions.Compose(3189, 6833, 341, null, "Drizzle"));
    }

    // Half a weather read is not a weather read: naming the temperature while silently dropping the
    // sky would leave "8 degrees" sounding like a complete, dry-day answer.
    [Fact]
    public void Compose_UnmappedPrecipitationDegradesTheWholeWeatherClause()
    {
        Assert.Equal("Attendance: 3,189, 47% of capacity. 341 away fans. weather unavailable.",
            MatchConditions.Compose(3189, 6833, 341, 8f, "MaxPrecipitationType"));
    }

    [Fact]
    public void Compose_UnreadableAwayFansIsMarkedNotSkipped()
    {
        Assert.Equal("Attendance: 3,189, 47% of capacity. away fans unavailable. 8 degrees, drizzle.",
            MatchConditions.Compose(3189, 6833, null, 8f, "Drizzle"));
    }

    // A missing capacity must not cost the attendance itself, only the share of it.
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Compose_MissingCapacityDropsOnlyThePercentage(int? capacity)
    {
        Assert.Equal("Attendance: 3,189. 341 away fans. 8 degrees, drizzle.",
            MatchConditions.Compose(3189, capacity, 341, 8f, "Drizzle"));
    }

    [Fact]
    public void Compose_NothingReadableReturnsNullSoTheCallerCanDegrade()
    {
        Assert.Null(MatchConditions.Compose(null, null, null, null, null));
    }

    [Fact]
    public void Compose_PercentageRoundsToNearestWholeNumber()
    {
        // 5,000 of 8,000 is 62.5%, which must round up rather than truncate to 62.
        Assert.StartsWith("Attendance: 5,000, 63% of capacity.",
            MatchConditions.Compose(5000, 8000, 100, 8f, "Dry"));
    }

    [Fact]
    public void Compose_AttendanceOverCapacityReportsTheTruth()
    {
        Assert.StartsWith("Attendance: 8,200, 103% of capacity.",
            MatchConditions.Compose(8200, 8000, 100, 8f, "Dry"));
    }

    [Theory]
    [InlineData(0f, "0 degrees")]
    [InlineData(1f, "1 degree")]
    [InlineData(1.4f, "1 degree")]
    [InlineData(-1f, "minus 1 degree")]
    [InlineData(-4f, "minus 4 degrees")]
    [InlineData(21.6f, "22 degrees")]
    public void Compose_TemperatureWordingAndSign(float celsius, string expected)
    {
        Assert.EndsWith(expected + ", dry.", MatchConditions.Compose(1, 2, 0, celsius, "Dry"));
    }

    [Theory]
    [InlineData("Dry", "dry")]
    [InlineData("Wet", "wet")]
    [InlineData("Drizzle", "drizzle")]
    [InlineData("Shower", "showers")]
    [InlineData("Rain", "rain")]
    [InlineData("DownPour", "heavy rain")]
    [InlineData("Sleet", "sleet")]
    [InlineData("Hail", "hail")]
    [InlineData("LightSnow", "light snow")]
    [InlineData("HeavySnow", "heavy snow")]
    public void Compose_EveryPrecipitationTypeIsNamed(string engineName, string spoken)
    {
        Assert.EndsWith("8 degrees, " + spoken + ".",
            MatchConditions.Compose(3189, 6833, 341, 8f, engineName));
    }

    [Fact]
    public void ComposeCrowdMood_MatchesTheVerifiedLiveReading()
    {
        Assert.Equal("home crowd idle happy, away crowd minor appreciation",
            MatchConditions.ComposeCrowdMood("IdleHappy", "MinorAppreciation"));
    }

    [Theory]
    [InlineData("IdleNeutral", "idle neutral")]
    [InlineData("CelebratingWildly", "celebrating wildly")]
    [InlineData("ConcedeVitalGoal", "concede vital goal")]
    [InlineData("MissedChance", "missed chance")]
    [InlineData("Singing", "singing")]
    [InlineData("Goading", "goading")]
    public void ComposeCrowdMood_SpellsEngineNamesAsWords(string engineName, string spoken)
    {
        Assert.StartsWith("home crowd " + spoken + ",",
            MatchConditions.ComposeCrowdMood(engineName, "Singing"));
    }

    [Fact]
    public void ComposeCrowdMood_OneEndUnreadableIsMarked()
    {
        Assert.Equal("home crowd unavailable, away crowd singing",
            MatchConditions.ComposeCrowdMood(null, "Singing"));
    }

    // An enum value the game added since this build would ToString as its number; naming it
    // "4" would be meaningless, and treating it as a mood would be a guess.
    [Fact]
    public void ComposeCrowdMood_NumericNameIsTreatedAsUnreadable()
    {
        Assert.Equal("home crowd unavailable, away crowd singing",
            MatchConditions.ComposeCrowdMood("31", "Singing"));
    }

    [Fact]
    public void ComposeCrowdMood_NeitherEndReadableReturnsNull()
    {
        Assert.Null(MatchConditions.ComposeCrowdMood(null, null));
    }
}
