'use client';

import { useEffect, useState } from 'react';
import { Channel, ChannelDetail, channelsApi } from '@/lib/api';
import { 
  MessageCircle, 
  Send, 
  Settings, 
  Power, 
  PowerOff, 
  RefreshCw,
  CheckCircle2,
  XCircle,
  AlertCircle,
  Loader2
} from 'lucide-react';

// Channel icons
const channelIcons: Record<string, React.ReactNode> = {
  whatsapp: <MessageCircle className="w-6 h-6 text-green-500" />,
  telegram: <Send className="w-6 h-6 text-blue-500" />,
  slack: <MessageCircle className="w-6 h-6 text-purple-500" />,
  discord: <MessageCircle className="w-6 h-6 text-indigo-500" />,
};

export default function ChannelsPage() {
  const [channels, setChannels] = useState<Channel[]>([]);
  const [selectedChannel, setSelectedChannel] = useState<ChannelDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [showConfigModal, setShowConfigModal] = useState(false);
  const [configValues, setConfigValues] = useState<Record<string, string>>({});

  useEffect(() => {
    loadChannels();
  }, []);

  const loadChannels = async () => {
    try {
      setLoading(true);
      const data = await channelsApi.list();
      setChannels(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load channels');
    } finally {
      setLoading(false);
    }
  };

  const handleSelectChannel = async (channelId: string) => {
    try {
      setActionLoading(channelId);
      const detail = await channelsApi.get(channelId);
      setSelectedChannel(detail);
      setConfigValues(detail.config || {});
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load channel details');
    } finally {
      setActionLoading(null);
    }
  };

  const handleConnect = async (channelId: string) => {
    try {
      setActionLoading(channelId);
      await channelsApi.connect(channelId);
      await loadChannels();
      if (selectedChannel?.id === channelId) {
        await handleSelectChannel(channelId);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to connect channel');
    } finally {
      setActionLoading(null);
    }
  };

  const handleDisconnect = async (channelId: string) => {
    try {
      setActionLoading(channelId);
      await channelsApi.disconnect(channelId);
      await loadChannels();
      if (selectedChannel?.id === channelId) {
        await handleSelectChannel(channelId);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to disconnect channel');
    } finally {
      setActionLoading(null);
    }
  };

  const handleSaveConfig = async () => {
    if (!selectedChannel) return;
    
    try {
      setActionLoading(selectedChannel.id);
      await channelsApi.updateConfig(selectedChannel.id, {
        enabled: true,
        config: configValues,
      });
      setShowConfigModal(false);
      await loadChannels();
      await handleSelectChannel(selectedChannel.id);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save configuration');
    } finally {
      setActionLoading(null);
    }
  };

  const handleToggleEnabled = async (channel: Channel) => {
    try {
      setActionLoading(channel.id);
      await channelsApi.updateConfig(channel.id, {
        enabled: !channel.isEnabled,
      });
      await loadChannels();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update channel');
    } finally {
      setActionLoading(null);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <Loader2 className="w-8 h-8 animate-spin text-blue-500" />
      </div>
    );
  }

  return (
    <div className="p-6">
      <div className="flex justify-between items-center mb-6">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Messaging Channels</h1>
          <p className="text-gray-700">Manage your connected messaging platforms</p>
        </div>
        <button
          onClick={loadChannels}
          className="flex items-center gap-2 px-4 py-2 bg-white border border-gray-300 rounded-lg hover:bg-gray-50"
        >
          <RefreshCw className="w-4 h-4" />
          Refresh
        </button>
      </div>

      {error && (
        <div className="mb-4 p-4 bg-red-50 border border-red-200 rounded-lg flex items-center gap-2 text-red-700">
          <AlertCircle className="w-5 h-5" />
          {error}
          <button onClick={() => setError(null)} className="ml-auto text-red-500 hover:text-red-700">
            &times;
          </button>
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        {channels.map((channel) => (
          <div
            key={channel.id}
            className={`bg-white rounded-lg border p-4 cursor-pointer transition-all ${
              selectedChannel?.id === channel.id ? 'border-blue-500 ring-2 ring-blue-200' : 'border-gray-200 hover:border-gray-300'
            }`}
            onClick={() => handleSelectChannel(channel.id)}
          >
            <div className="flex items-center justify-between mb-3">
              <div className="flex items-center gap-3">
                {channelIcons[channel.id] || <MessageCircle className="w-6 h-6 text-gray-500" />}
                <div>
                  <h3 className="font-semibold text-gray-900">{channel.label}</h3>
                  <p className="text-xs text-gray-600">{channel.id}</p>
                </div>
              </div>
              {actionLoading === channel.id ? (
                <Loader2 className="w-5 h-5 animate-spin text-gray-500" />
              ) : channel.isConnected ? (
                <CheckCircle2 className="w-5 h-5 text-green-500" />
              ) : channel.lastError ? (
                <XCircle className="w-5 h-5 text-red-500" />
              ) : (
                <div className="w-5 h-5 rounded-full bg-gray-200" />
              )}
            </div>

            <p className="text-sm text-gray-600 mb-3 line-clamp-2">{channel.description}</p>

            <div className="flex items-center justify-between">
              <span className={`text-xs px-2 py-1 rounded-full ${
                channel.isConnected
                  ? 'bg-green-100 text-green-700'
                  : channel.isEnabled
                  ? 'bg-yellow-100 text-yellow-700'
                  : 'bg-gray-100 text-gray-600'
              }`}>
                {channel.isConnected ? 'Connected' : channel.isEnabled ? 'Enabled' : 'Disabled'}
              </span>
              {channel.accountName && (
                <span className="text-xs text-gray-600">{channel.accountName}</span>
              )}
            </div>
          </div>
        ))}
      </div>

      {selectedChannel && (
        <div className="bg-white rounded-lg border border-gray-200 p-6">
          <div className="flex items-center justify-between mb-6">
            <div className="flex items-center gap-4">
              {channelIcons[selectedChannel.id] || <MessageCircle className="w-8 h-8 text-gray-500" />}
              <div>
                <h2 className="text-xl font-bold text-gray-900">{selectedChannel.label}</h2>
                <p className="text-gray-600">{selectedChannel.description}</p>
              </div>
            </div>
            <div className="flex items-center gap-2">
              <button
                onClick={() => setShowConfigModal(true)}
                className="flex items-center gap-2 px-4 py-2 bg-gray-100 rounded-lg hover:bg-gray-200"
              >
                <Settings className="w-4 h-4" />
                Configure
              </button>
              {selectedChannel.isConnected ? (
                <button
                  onClick={() => handleDisconnect(selectedChannel.id)}
                  disabled={actionLoading === selectedChannel.id}
                  className="flex items-center gap-2 px-4 py-2 bg-red-100 text-red-700 rounded-lg hover:bg-red-200 disabled:opacity-50"
                >
                  {actionLoading === selectedChannel.id ? (
                    <Loader2 className="w-4 h-4 animate-spin" />
                  ) : (
                    <PowerOff className="w-4 h-4" />
                  )}
                  Disconnect
                </button>
              ) : (
                <button
                  onClick={() => handleConnect(selectedChannel.id)}
                  disabled={actionLoading === selectedChannel.id}
                  className="flex items-center gap-2 px-4 py-2 bg-green-100 text-green-700 rounded-lg hover:bg-green-200 disabled:opacity-50"
                >
                  {actionLoading === selectedChannel.id ? (
                    <Loader2 className="w-4 h-4 animate-spin" />
                  ) : (
                    <Power className="w-4 h-4" />
                  )}
                  Connect
                </button>
              )}
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div>
              <h3 className="font-semibold text-gray-900 mb-3">Status</h3>
              <dl className="space-y-2">
                <div className="flex justify-between">
                  <dt className="text-gray-600">Connection</dt>
                  <dd className={selectedChannel.isConnected ? 'text-green-600' : 'text-gray-600'}>
                    {selectedChannel.isConnected ? 'Connected' : 'Disconnected'}
                  </dd>
                </div>
                <div className="flex justify-between">
                  <dt className="text-gray-600">Enabled</dt>
                  <dd>
                    <button
                      onClick={() => handleToggleEnabled(selectedChannel)}
                      className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                        selectedChannel.isEnabled ? 'bg-blue-600' : 'bg-gray-200'
                      }`}
                    >
                      <span
                        className={`inline-block h-4 w-4 transform rounded-full bg-white transition-transform ${
                          selectedChannel.isEnabled ? 'translate-x-6' : 'translate-x-1'
                        }`}
                      />
                    </button>
                  </dd>
                </div>
                {selectedChannel.accountName && (
                  <div className="flex justify-between">
                    <dt className="text-gray-600">Account</dt>
                    <dd className="text-gray-900">{selectedChannel.accountName}</dd>
                  </div>
                )}
                {selectedChannel.connectedSince && (
                  <div className="flex justify-between">
                    <dt className="text-gray-600">Connected Since</dt>
                    <dd className="text-gray-900">
                      {new Date(selectedChannel.connectedSince).toLocaleString()}
                    </dd>
                  </div>
                )}
                {selectedChannel.lastError && (
                  <div className="mt-3 p-3 bg-red-50 rounded-lg">
                    <p className="text-sm text-red-700">{selectedChannel.lastError}</p>
                  </div>
                )}
              </dl>
            </div>

            <div>
              <h3 className="font-semibold text-gray-900 mb-3">Capabilities</h3>
              <div className="grid grid-cols-2 gap-2">
                {[
                  { label: 'Media', value: selectedChannel.capabilities.supportsMedia },
                  { label: 'Threads', value: selectedChannel.capabilities.supportsThreads },
                  { label: 'Reactions', value: selectedChannel.capabilities.supportsReactions },
                  { label: 'Edits', value: selectedChannel.capabilities.supportsEdits },
                  { label: 'Deletes', value: selectedChannel.capabilities.supportsDeletes },
                  { label: 'Voice Notes', value: selectedChannel.capabilities.supportsVoiceNotes },
                ].map((cap) => (
                  <div key={cap.label} className="flex items-center gap-2">
                    {cap.value ? (
                      <CheckCircle2 className="w-4 h-4 text-green-500" />
                    ) : (
                      <XCircle className="w-4 h-4 text-gray-400" />
                    )}
                    <span className={cap.value ? 'text-gray-900' : 'text-gray-500'}>
                      {cap.label}
                    </span>
                  </div>
                ))}
              </div>
              <p className="mt-3 text-sm text-gray-600">
                Max message length: {selectedChannel.capabilities.maxMessageLength.toLocaleString()} chars
              </p>
            </div>
          </div>
        </div>
      )}

      {/* Configuration Modal */}
      {showConfigModal && selectedChannel && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white rounded-lg w-full max-w-md p-6">
            <h2 className="text-xl font-bold text-gray-900 mb-4">
              Configure {selectedChannel.label}
            </h2>
            
            <div className="space-y-4">
              {selectedChannel.configSchema.map((field) => (
                <div key={field.key}>
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    {field.label}
                    {field.isRequired && <span className="text-red-500 ml-1">*</span>}
                  </label>
                  {field.description && (
                    <p className="text-xs text-gray-600 mb-1">{field.description}</p>
                  )}
                  <input
                    type={field.isSensitive ? 'password' : 'text'}
                    value={configValues[field.key] || ''}
                    onChange={(e) => setConfigValues({ ...configValues, [field.key]: e.target.value })}
                    placeholder={field.defaultValue || ''}
                    className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
                  />
                </div>
              ))}
            </div>

            <div className="flex justify-end gap-2 mt-6">
              <button
                onClick={() => setShowConfigModal(false)}
                className="px-4 py-2 text-gray-700 hover:bg-gray-100 rounded-lg"
              >
                Cancel
              </button>
              <button
                onClick={handleSaveConfig}
                disabled={actionLoading === selectedChannel.id}
                className="flex items-center gap-2 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50"
              >
                {actionLoading === selectedChannel.id && (
                  <Loader2 className="w-4 h-4 animate-spin" />
                )}
                Save Configuration
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
