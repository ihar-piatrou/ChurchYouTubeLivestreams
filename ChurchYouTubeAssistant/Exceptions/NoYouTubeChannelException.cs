namespace ChurchYouTubeAssistant.Exceptions;

/// <summary>
/// The authorization succeeded but the Google account has no YouTube channel, so channels.list
/// with mine=true returned nothing. The admin authorized the wrong Google account, or the account
/// has never created a channel.
/// </summary>
public sealed class NoYouTubeChannelException(string message) : Exception(message);
