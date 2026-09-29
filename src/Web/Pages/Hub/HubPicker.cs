using Application.Network.HubScan;

namespace Web.Pages.Hub;

/// <summary>What the hub picker shows: the page each hub opens, and the question asked.</summary>
public sealed record HubPicker(string Page, string Title, string Question, IReadOnlyList<HubChoice> Hubs, string? Mode = null);
