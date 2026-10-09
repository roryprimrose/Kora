namespace Kora.Application.Dependencies;

/// <summary>Host-issued one-use confirmation, bound to the exact reviewed offer.</summary>
public sealed class ModelHandoffReview
{
    internal ModelHandoffReview(ModelHandoffOffer offer) => Offer = offer;
    public ModelHandoffOffer Offer { get; }
    internal int Used;
}
