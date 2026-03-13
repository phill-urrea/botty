'use client';

import { useEffect, useState } from 'react';
import { Header } from '@/components/layout/header';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { kanbanApi, skillsApi, memoryApi, schedulerApi, whatsappApi, healthApi } from '@/lib/api';
import { Activity, Brain, CheckCircle, Clock, MessageSquare, Plug, AlertCircle } from 'lucide-react';

interface DashboardStats {
  tasks: { total: number; pending: number; inProgress: number; needsApproval: number };
  skills: { total: number; configured: number };
  memories: { total: number };
  scheduledTasks: { total: number; enabled: number };
  whatsapp: { connected: boolean };
  apiHealth: boolean;
}

export default function Dashboard() {
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    async function loadStats() {
      try {
        const [tasksRes, skillsRes, schedulerRes] = await Promise.all([
          kanbanApi.getTasks().catch(() => ({ tasks: [] })),
          skillsApi.list().catch(() => ({ skills: [] })),
          schedulerApi.list().catch(() => ({ tasks: [] })),
        ]);

        // Try to get WhatsApp status, but don't fail if unavailable
        let whatsappConnected = false;
        try {
          const whatsappRes = await whatsappApi.getStatus();
          whatsappConnected = whatsappRes.connected;
        } catch {
          // WhatsApp service might not be running
        }

        // Check API health
        let apiHealth = false;
        try {
          await healthApi.check();
          apiHealth = true;
        } catch {
          // API might not be running
        }

        const tasks = tasksRes.tasks || [];
        const skills = skillsRes.skills || [];
        const scheduled = schedulerRes.tasks || [];

        setStats({
          tasks: {
            total: tasks.length,
            pending: tasks.filter(t => t.lane === 'ToDo').length,
            inProgress: tasks.filter(t => t.lane === 'InProgress').length,
            needsApproval: tasks.filter(t => t.lane === 'NeedsApproval').length,
          },
          skills: {
            total: skills.length,
            configured: skills.filter(s => s.isConfigured).length,
          },
          memories: { total: 0 }, // Would need a count endpoint
          scheduledTasks: {
            total: scheduled.length,
            enabled: scheduled.filter(t => t.isActive).length,
          },
          whatsapp: { connected: whatsappConnected },
          apiHealth,
        });
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load dashboard');
      } finally {
        setLoading(false);
      }
    }

    loadStats();
  }, []);

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      <Header title="Dashboard" description="Overview of your AI assistant" />
      
      <div className="flex-1 p-6 space-y-6">
        {error && (
          <div className="rounded-lg bg-yellow-50 border border-yellow-200 p-4 flex items-center gap-3">
            <AlertCircle className="h-5 w-5 text-yellow-600" />
            <span className="text-yellow-800">Some services may be unavailable: {error}</span>
          </div>
        )}

        {/* Status Cards */}
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
          <Card>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium text-gray-900">API Status</CardTitle>
              <Activity className="h-4 w-4 text-gray-500" />
            </CardHeader>
            <CardContent>
              <div className="flex items-center gap-2">
                <div className={`h-3 w-3 rounded-full ${stats?.apiHealth ? 'bg-green-500' : 'bg-red-500'}`} />
                <span className="text-2xl font-bold text-gray-900">{stats?.apiHealth ? 'Online' : 'Offline'}</span>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium text-gray-900">WhatsApp</CardTitle>
              <MessageSquare className="h-4 w-4 text-gray-500" />
            </CardHeader>
            <CardContent>
              <div className="flex items-center gap-2">
                <div className={`h-3 w-3 rounded-full ${stats?.whatsapp.connected ? 'bg-green-500' : 'bg-gray-300'}`} />
                <span className="text-2xl font-bold text-gray-900">{stats?.whatsapp.connected ? 'Connected' : 'Disconnected'}</span>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium text-gray-900">Skills</CardTitle>
              <Plug className="h-4 w-4 text-gray-500" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold text-gray-900">{stats?.skills.configured}/{stats?.skills.total}</div>
              <p className="text-xs text-gray-600">configured</p>
            </CardContent>
          </Card>

          <Card>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium text-gray-900">Scheduled Tasks</CardTitle>
              <Clock className="h-4 w-4 text-gray-500" />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold text-gray-900">{stats?.scheduledTasks.enabled}/{stats?.scheduledTasks.total}</div>
              <p className="text-xs text-gray-600">active</p>
            </CardContent>
          </Card>
        </div>

        {/* Kanban Overview */}
        <div className="grid gap-4 md:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle>Task Overview</CardTitle>
              <CardDescription>Current state of the Kanban board</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="space-y-4">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <div className="h-3 w-3 rounded bg-gray-400" />
                    <span className="text-sm font-medium text-gray-900">To Do</span>
                  </div>
                  <Badge variant="secondary">{stats?.tasks.pending || 0}</Badge>
                </div>
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <div className="h-3 w-3 rounded bg-blue-500" />
                    <span className="text-sm font-medium text-gray-900">In Progress</span>
                  </div>
                  <Badge variant="default">{stats?.tasks.inProgress || 0}</Badge>
                </div>
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <div className="h-3 w-3 rounded bg-yellow-500" />
                    <span className="text-sm font-medium text-gray-900">Needs Approval</span>
                  </div>
                  <Badge variant="warning">{stats?.tasks.needsApproval || 0}</Badge>
                </div>
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <CheckCircle className="h-3 w-3 text-green-500" />
                    <span className="text-sm font-medium text-gray-900">Total Tasks</span>
                  </div>
                  <Badge variant="outline">{stats?.tasks.total || 0}</Badge>
                </div>
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Quick Actions</CardTitle>
              <CardDescription>Common administrative tasks</CardDescription>
            </CardHeader>
            <CardContent>
              <div className="space-y-2">
                <a href="/kanban" className="block p-3 rounded-lg border border-gray-200 hover:bg-gray-50 transition-colors">
                  <div className="font-medium text-gray-900">Review Approvals</div>
                  <div className="text-sm text-gray-600">Check tasks waiting for your approval</div>
                </a>
                <a href="/soul" className="block p-3 rounded-lg border border-gray-200 hover:bg-gray-50 transition-colors">
                  <div className="font-medium text-gray-900">Edit Soul Config</div>
                  <div className="text-sm text-gray-600">Customize your assistant&apos;s personality</div>
                </a>
                <a href="/skills" className="block p-3 rounded-lg border border-gray-200 hover:bg-gray-50 transition-colors">
                  <div className="font-medium text-gray-900">Configure Skills</div>
                  <div className="text-sm text-gray-600">Set up Gmail, Calendar, and more</div>
                </a>
              </div>
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
