namespace RimWorks.RimObs.Observers;

internal interface ITpsFpsSink {
    void RecordTpsFps(in TpsFpsSample sample);
}
