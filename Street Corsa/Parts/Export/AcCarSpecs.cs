namespace StreetCorsa.Parts.Export;

/// <summary>One axle of a car as its Assetto Corsa data describes it</summary>
public sealed record AcAxleSpecs
{
    /// <summary>Tread width, rolling radius and rim radius of the first compound, metres</summary>
    public double TyreWidth { get; init; }
    public double TyreRadius { get; init; }
    public double RimRadius { get; init; }

    /// <summary>Brake torque one wheel of the axle gets at full pedal, Nm</summary>
    public double BrakeTorque { get; init; }

    /// <summary>Wheel rate, N/m</summary>
    public double SpringRate { get; init; }

    public double DampBump { get; init; }
    public double DampRebound { get; init; }

    /// <summary>Kilograms the axle carries at rest, per wheel</summary>
    public double CornerLoad { get; init; }

    /// <summary>suspensions.ini's TYPE: DWB, STRUT, AXLE (rear only), ML, COSMIC; null when the car does not say</summary>
    public string? SuspensionType { get; init; }

    /// <summary>Whether brakes.ini gives this axle's brakes temperatures ([TEMPS_FRONT]/[TEMPS_REAR]): its author's fade</summary>
    public bool HasBrakeTemps { get; init; }

    /// <summary>The axle's sway bar, N/m (suspensions.ini [ARB] FRONT/REAR); 0 for none</summary>
    public double SwayBarRate { get; init; }
}

/// <summary>
/// What a car's own data says about the running gear the mapping starts from. A car's factory parts are
/// matched against these, and every change the player makes moves the car's figures in proportion.
/// </summary>
public sealed record AcCarSpecs
{
    public double TotalMass { get; init; }

    /// <summary>Share of the mass on the front axle</summary>
    public double FrontWeightShare { get; init; }

    /// <summary>drivetrain.ini's [TRACTION] TYPE: RWD, FWD, AWD or AWD2; null when the car does not say</summary>
    public string? Traction { get; init; }

    public AcAxleSpecs Front { get; init; } = new();
    public AcAxleSpecs Rear { get; init; } = new();

    /// <summary>drivetrain.ini's [GEARS] FINAL, 0 when the car does not say</summary>
    public double FinalRatio { get; init; }

    /// <summary>drivetrain.ini's [DIFFERENTIAL] POWER, COAST and PRELOAD (Nm)</summary>
    public (double Power, double Coast, double Preload) Differential { get; init; }

    /// <summary>The most torque of the car's own engine curve (power.lut, or the file engine.ini names), Nm; 0 when none reads</summary>
    public double PeakTorque { get; init; }

    /// <summary>Whether the rear wheels hang on a live axle (suspensions.ini [REAR] TYPE=AXLE): leaf springs and traction bars fit</summary>
    public bool HasLiveAxle => AcCarIni.IsLiveAxle(Rear.SuspensionType);

    /// <summary>Whether the rear wheels are driven (RWD, or four-wheel drive)</summary>
    public bool DrivesRear => Traction is null or "RWD" or "AWD" or "AWD2";

    public AcAxleSpecs Axle(bool front) => front ? Front : Rear;

    /// <param name="readFile">Text of a file of the car's data, null when it has no such file</param>
    public static AcCarSpecs Read(Func<string, string?> readFile)
    {
        var car = new IniText(readFile("car.ini"));
        var tyres = new IniText(readFile("tyres.ini"));
        var brakes = new IniText(readFile("brakes.ini"));
        var suspensions = new IniText(readFile("suspensions.ini"));
        var drivetrain = new IniText(readFile("drivetrain.ini"));

        var mass = car.GetNumber("BASIC", "TOTALMASS") ?? 0;
        var frontShare = suspensions.GetNumber("BASIC", "CG_LOCATION") ?? 0.5;

        var brakeTorque = brakes.GetNumber("DATA", "MAX_TORQUE") ?? 0;
        var brakeShare = brakes.GetNumber("DATA", "FRONT_SHARE") ?? 0.6;

        AcAxleSpecs Axle(string section, bool front)
        {
            var (rateSection, rateKey) = AcCarIni.SpringRateKey(suspensions, section);
            return new AcAxleSpecs
            {
                TyreWidth = tyres.GetNumber(section, "WIDTH") ?? 0,
                TyreRadius = tyres.GetNumber(section, "RADIUS") ?? 0,
                RimRadius = tyres.GetNumber(section, "RIM_RADIUS") ?? 0,
                BrakeTorque = brakeTorque * (front ? brakeShare : 1 - brakeShare),
                SpringRate = suspensions.GetNumber(rateSection, rateKey) ?? 0,
                DampBump = suspensions.GetNumber(section, "DAMP_BUMP") ?? 0,
                DampRebound = suspensions.GetNumber(section, "DAMP_REBOUND") ?? 0,
                CornerLoad = mass * (front ? frontShare : 1 - frontShare) / 2,
                SuspensionType = suspensions.Get(section, "TYPE")?.Trim().ToUpperInvariant() is { Length: > 0 } type ? type : null,
                HasBrakeTemps = brakes.Sections.Contains(front ? "TEMPS_FRONT" : "TEMPS_REAR"),
                SwayBarRate = Math.Max(0, suspensions.GetNumber("ARB", section) ?? 0)
            };
        }

        return new AcCarSpecs
        {
            TotalMass = mass,
            FrontWeightShare = frontShare,
            Traction = drivetrain.Get("TRACTION", "TYPE")?.Trim().ToUpperInvariant() is { Length: > 0 } traction ? traction : null,
            Front = Axle("FRONT", true),
            Rear = Axle("REAR", false),
            FinalRatio = drivetrain.GetNumber("GEARS", "FINAL") ?? 0,
            Differential = (drivetrain.GetNumber("DIFFERENTIAL", "POWER") ?? 0, drivetrain.GetNumber("DIFFERENTIAL", "COAST") ?? 0,
                drivetrain.GetNumber("DIFFERENTIAL", "PRELOAD") ?? 0),
            PeakTorque = AcPowerCurve.Points(readFile(AcPowerCurve.FileName(new IniText(readFile("engine.ini")))))
                .Select(p => p.Torque).Append(0).Max()
        };
    }
}
