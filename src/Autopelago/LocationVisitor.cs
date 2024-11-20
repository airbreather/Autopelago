namespace Autopelago;

public abstract class LocationVisitor
{
    public delegate bool VisitLocationDelegate(
        LocationDefinitionModel currentLocation,
        LocationDefinitionModel previousLocation,
        int distance,
        bool alreadyChecked);

    public static LocationVisitor Create(VisitLocationDelegate visitLocation)
    {
        return new AnonymousLocationVisitor(visitLocation);
    }

    public abstract bool VisitLocation(
        LocationDefinitionModel currentLocation,
        LocationDefinitionModel previousLocation,
        int distance,
        bool alreadyChecked);

    private sealed class AnonymousLocationVisitor : LocationVisitor
    {
        private readonly VisitLocationDelegate _visitLocation;

        public AnonymousLocationVisitor(VisitLocationDelegate visitLocation)
        {
            _visitLocation = visitLocation;
        }

        public override bool VisitLocation(
            LocationDefinitionModel currentLocation,
            LocationDefinitionModel previousLocation,
            int distance,
            bool alreadyChecked)
        {
            return _visitLocation(
                currentLocation,
                previousLocation,
                distance,
                alreadyChecked);
        }
    }
}
