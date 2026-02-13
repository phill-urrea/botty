'use client';

import { useEffect, useState, useCallback } from 'react';
import { Header } from '@/components/layout/header';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Badge } from '@/components/ui/badge';
import { schedulerApi, ScheduledTask } from '@/lib/api';
import { formatDate, formatRelativeTime } from '@/lib/utils';
import { Plus, Clock, Play, Pause, Trash2, RefreshCw, X, Calendar } from 'lucide-react';

export default function SchedulerPage() {
  const [tasks, setTasks] = useState<ScheduledTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [showCreate, setShowCreate] = useState(false);
  const [newTask, setNewTask] = useState({
    name: '',
    description: '',
    cronExpression: '',
    taskType: 'General',
    taskPayload: '',
    isEnabled: true,
  });

  const loadTasks = useCallback(async () => {
    try {
      setLoading(true);
      const response = await schedulerApi.list();
      setTasks(response.tasks || []);
    } catch (error) {
      console.error('Failed to load scheduled tasks:', error);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadTasks();
  }, [loadTasks]);

  const handleCreate = async () => {
    try {
      await schedulerApi.create(newTask);
      setShowCreate(false);
      setNewTask({
        name: '',
        description: '',
        cronExpression: '',
        taskType: 'General',
        taskPayload: '',
        isEnabled: true,
      });
      loadTasks();
    } catch (error) {
      console.error('Failed to create task:', error);
    }
  };

  const handleToggle = async (task: ScheduledTask) => {
    try {
      if (task.isEnabled) {
        await schedulerApi.disable(task.id);
      } else {
        await schedulerApi.enable(task.id);
      }
      loadTasks();
    } catch (error) {
      console.error('Failed to toggle task:', error);
    }
  };

  const handleDelete = async (id: string) => {
    if (!confirm('Are you sure you want to delete this scheduled task?')) return;
    
    try {
      await schedulerApi.delete(id);
      loadTasks();
    } catch (error) {
      console.error('Failed to delete task:', error);
    }
  };

  const handleRunNow = async (id: string) => {
    try {
      await schedulerApi.runNow(id);
      alert('Task triggered successfully');
    } catch (error) {
      console.error('Failed to run task:', error);
    }
  };

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      <Header title="Scheduler" description="Manage scheduled tasks and cron jobs" />

      <div className="p-4 border-b border-gray-200 bg-white flex items-center justify-between">
        <Button onClick={() => setShowCreate(true)}>
          <Plus className="h-4 w-4 mr-2" />
          New Scheduled Task
        </Button>
        <Button variant="outline" onClick={loadTasks}>
          <RefreshCw className="h-4 w-4 mr-2" />
          Refresh
        </Button>
      </div>

      <div className="flex-1 overflow-auto p-6">
        {tasks.length > 0 ? (
          <div className="space-y-4">
            {tasks.map((task) => (
              <Card key={task.id}>
                <CardContent className="p-4">
                  <div className="flex items-start justify-between">
                    <div className="flex-1">
                      <div className="flex items-center gap-3 mb-2">
                        <h3 className="font-semibold text-lg">{task.name}</h3>
                        {task.isEnabled ? (
                          <Badge variant="success">Active</Badge>
                        ) : (
                          <Badge variant="secondary">Disabled</Badge>
                        )}
                        <Badge variant="outline">{task.taskType}</Badge>
                      </div>
                      {task.description && (
                        <p className="text-gray-700 mb-3">{task.description}</p>
                      )}
                      <div className="flex items-center gap-6 text-sm text-gray-600">
                        <div className="flex items-center gap-1">
                          <Clock className="h-4 w-4" />
                          <code className="bg-gray-100 px-2 py-0.5 rounded">{task.cronExpression}</code>
                        </div>
                        {task.nextRunAt && (
                          <div className="flex items-center gap-1">
                            <Calendar className="h-4 w-4" />
                            Next: {formatRelativeTime(task.nextRunAt)}
                          </div>
                        )}
                        {task.lastRunAt && (
                          <div>
                            Last run: {formatRelativeTime(task.lastRunAt)}
                          </div>
                        )}
                      </div>
                    </div>
                    <div className="flex items-center gap-2">
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleRunNow(task.id)}
                        title="Run now"
                      >
                        <Play className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleToggle(task)}
                        title={task.isEnabled ? 'Disable' : 'Enable'}
                      >
                        {task.isEnabled ? (
                          <Pause className="h-4 w-4" />
                        ) : (
                          <Play className="h-4 w-4" />
                        )}
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        className="text-red-600 hover:text-red-700 hover:bg-red-50"
                        onClick={() => handleDelete(task.id)}
                        title="Delete"
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        ) : (
          <div className="flex flex-col items-center justify-center h-full text-gray-600">
            <Clock className="h-12 w-12 mb-4" />
            <p className="text-lg font-medium">No scheduled tasks</p>
            <p className="text-sm">Create a task to automate recurring actions</p>
          </div>
        )}
      </div>

      {/* Create Task Modal */}
      {showCreate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div className="absolute inset-0 bg-black/50" onClick={() => setShowCreate(false)} />
          <div className="relative bg-white rounded-lg shadow-xl w-full max-w-lg mx-4">
            <div className="flex items-center justify-between p-4 border-b">
              <h2 className="text-lg font-semibold">Create Scheduled Task</h2>
              <Button variant="ghost" size="icon" onClick={() => setShowCreate(false)}>
                <X className="h-4 w-4" />
              </Button>
            </div>

            <div className="p-4 space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Name <span className="text-red-500">*</span>
                </label>
                <Input
                  value={newTask.name}
                  onChange={(e) => setNewTask(prev => ({ ...prev, name: e.target.value }))}
                  placeholder="Daily reminder"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Description
                </label>
                <Textarea
                  value={newTask.description}
                  onChange={(e) => setNewTask(prev => ({ ...prev, description: e.target.value }))}
                  placeholder="What does this task do?"
                  rows={2}
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Cron Expression <span className="text-red-500">*</span>
                </label>
                <Input
                  value={newTask.cronExpression}
                  onChange={(e) => setNewTask(prev => ({ ...prev, cronExpression: e.target.value }))}
                  placeholder="0 9 * * * (every day at 9 AM)"
                />
                <p className="text-xs text-gray-600 mt-1">
                  Format: minute hour day month weekday
                </p>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Task Type
                </label>
                <select
                  value={newTask.taskType}
                  onChange={(e) => setNewTask(prev => ({ ...prev, taskType: e.target.value }))}
                  className="w-full h-10 rounded-md border border-gray-300 px-3 text-sm"
                >
                  <option value="General">General</option>
                  <option value="Reminder">Reminder</option>
                  <option value="Maintenance">Maintenance</option>
                  <option value="Monitoring">Monitoring</option>
                </select>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Task Payload (JSON)
                </label>
                <Textarea
                  value={newTask.taskPayload}
                  onChange={(e) => setNewTask(prev => ({ ...prev, taskPayload: e.target.value }))}
                  placeholder='{"message": "Remember to check emails"}'
                  rows={3}
                />
              </div>
            </div>

            <div className="flex items-center justify-end gap-2 p-4 border-t bg-gray-50">
              <Button variant="outline" onClick={() => setShowCreate(false)}>
                Cancel
              </Button>
              <Button
                onClick={handleCreate}
                disabled={!newTask.name.trim() || !newTask.cronExpression.trim()}
              >
                Create Task
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
