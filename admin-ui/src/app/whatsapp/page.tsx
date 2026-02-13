'use client';

import { useEffect, useState, useCallback } from 'react';
import { Header } from '@/components/layout/header';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { whatsappApi, WhatsAppStatus } from '@/lib/api';
import { formatRelativeTime } from '@/lib/utils';
import { MessageSquare, QrCode, RefreshCw, Smartphone, Wifi, WifiOff } from 'lucide-react';

export default function WhatsAppPage() {
  const [status, setStatus] = useState<WhatsAppStatus | null>(null);
  const [qrCode, setQrCode] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadStatus = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const statusRes = await whatsappApi.getStatus();
      setStatus(statusRes);
      
      if (!statusRes.connected && statusRes.qrCode) {
        setQrCode(statusRes.qrCode);
      } else if (!statusRes.connected) {
        // Try to get QR code separately
        try {
          const qrRes = await whatsappApi.getQrCode();
          setQrCode(qrRes.qrCode);
        } catch {
          // QR code not available yet
        }
      } else {
        setQrCode(null);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load WhatsApp status');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadStatus();
    // Poll for status updates every 5 seconds
    const interval = setInterval(loadStatus, 5000);
    return () => clearInterval(interval);
  }, [loadStatus]);

  if (loading && !status) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      <Header title="WhatsApp" description="Manage WhatsApp connection and messaging" />

      <div className="p-4 border-b border-gray-200 bg-white flex items-center justify-between">
        <div className="flex items-center gap-3">
          {status?.connected ? (
            <>
              <div className="h-3 w-3 rounded-full bg-green-500 animate-pulse" />
              <span className="text-green-700 font-medium">Connected</span>
            </>
          ) : (
            <>
              <div className="h-3 w-3 rounded-full bg-yellow-500" />
              <span className="text-yellow-700 font-medium">Waiting for connection</span>
            </>
          )}
        </div>
        <Button variant="outline" onClick={loadStatus}>
          <RefreshCw className="h-4 w-4 mr-2" />
          Refresh
        </Button>
      </div>

      <div className="flex-1 overflow-auto p-6">
        {error && (
          <div className="mb-6 p-4 bg-red-50 border border-red-200 rounded-lg text-red-800">
            {error}
          </div>
        )}

        <div className="grid gap-6 md:grid-cols-2">
          {/* Connection Status */}
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                {status?.connected ? (
                  <Wifi className="h-5 w-5 text-green-500" />
                ) : (
                  <WifiOff className="h-5 w-5 text-gray-500" />
                )}
                Connection Status
              </CardTitle>
              <CardDescription>
                Current WhatsApp Web connection state
              </CardDescription>
            </CardHeader>
            <CardContent>
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <span className="text-gray-600">Status</span>
                  <Badge variant={status?.connected ? 'success' : 'warning'}>
                    {status?.connected ? 'Connected' : 'Disconnected'}
                  </Badge>
                </div>
                {status?.phoneNumber && (
                  <div className="flex items-center justify-between">
                    <span className="text-gray-600">Phone Number</span>
                    <span className="font-mono">{status.phoneNumber}</span>
                  </div>
                )}
                {status?.lastSeen && (
                  <div className="flex items-center justify-between">
                    <span className="text-gray-600">Last Active</span>
                    <span>{formatRelativeTime(status.lastSeen)}</span>
                  </div>
                )}
              </div>
            </CardContent>
          </Card>

          {/* QR Code */}
          {!status?.connected && (
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <QrCode className="h-5 w-5" />
                  Scan QR Code
                </CardTitle>
                <CardDescription>
                  Open WhatsApp on your phone and scan this QR code to connect
                </CardDescription>
              </CardHeader>
              <CardContent>
                <div className="flex flex-col items-center justify-center p-4">
                  {qrCode ? (
                    <div className="bg-white p-4 rounded-lg shadow-inner">
                      <img
                        src={qrCode}
                        alt="WhatsApp QR Code"
                        className="w-48 h-48"
                      />
                    </div>
                  ) : (
                    <div className="w-48 h-48 bg-gray-100 rounded-lg flex items-center justify-center text-gray-500">
                      <div className="text-center">
                        <QrCode className="h-12 w-12 mx-auto mb-2" />
                        <p className="text-sm">Waiting for QR code...</p>
                      </div>
                    </div>
                  )}
                  <p className="text-sm text-gray-600 mt-4 text-center">
                    1. Open WhatsApp on your phone<br />
                    2. Tap Menu or Settings and select Linked Devices<br />
                    3. Point your phone at this screen to capture the code
                  </p>
                </div>
              </CardContent>
            </Card>
          )}

          {/* Instructions */}
          <Card className={status?.connected ? '' : 'md:col-span-2'}>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Smartphone className="h-5 w-5" />
                How it Works
              </CardTitle>
            </CardHeader>
            <CardContent>
              <div className="space-y-4">
                <div className="flex gap-3">
                  <div className="flex-shrink-0 w-8 h-8 rounded-full bg-blue-100 text-blue-600 flex items-center justify-center font-semibold">
                    1
                  </div>
                  <div>
                    <h4 className="font-medium">Connect WhatsApp</h4>
                    <p className="text-sm text-gray-600">
                      Scan the QR code with your phone to link this device
                    </p>
                  </div>
                </div>
                <div className="flex gap-3">
                  <div className="flex-shrink-0 w-8 h-8 rounded-full bg-blue-100 text-blue-600 flex items-center justify-center font-semibold">
                    2
                  </div>
                  <div>
                    <h4 className="font-medium">Receive Messages</h4>
                    <p className="text-sm text-gray-600">
                      Incoming messages are forwarded to the assistant for processing
                    </p>
                  </div>
                </div>
                <div className="flex gap-3">
                  <div className="flex-shrink-0 w-8 h-8 rounded-full bg-blue-100 text-blue-600 flex items-center justify-center font-semibold">
                    3
                  </div>
                  <div>
                    <h4 className="font-medium">Approve Responses</h4>
                    <p className="text-sm text-gray-600">
                      Review and approve draft responses before they are sent
                    </p>
                  </div>
                </div>
              </div>
            </CardContent>
          </Card>

          {/* Connected Features */}
          {status?.connected && (
            <Card>
              <CardHeader>
                <CardTitle className="flex items-center gap-2">
                  <MessageSquare className="h-5 w-5" />
                  Features
                </CardTitle>
              </CardHeader>
              <CardContent>
                <ul className="space-y-2 text-gray-900">
                  <li className="flex items-center gap-2">
                    <div className="h-2 w-2 rounded-full bg-green-500" />
                    <span>Receive incoming messages</span>
                  </li>
                  <li className="flex items-center gap-2">
                    <div className="h-2 w-2 rounded-full bg-green-500" />
                    <span>Draft response suggestions</span>
                  </li>
                  <li className="flex items-center gap-2">
                    <div className="h-2 w-2 rounded-full bg-green-500" />
                    <span>Send approved messages</span>
                  </li>
                  <li className="flex items-center gap-2">
                    <div className="h-2 w-2 rounded-full bg-green-500" />
                    <span>View conversation history</span>
                  </li>
                </ul>
              </CardContent>
            </Card>
          )}
        </div>
      </div>
    </div>
  );
}
