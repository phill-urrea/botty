'use client';

import { useEffect, useState, useCallback } from 'react';
import { Header } from '@/components/layout/header';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Badge } from '@/components/ui/badge';
import { schedulerApi, ScheduledTask } from '@/lib/api';
import { formatRelativeTime } from '@/lib/utils';
import { Plus, Clock, Play, Pause, Trash2, RefreshCw, X, Calendar, Pencil } from 'lucide-react';

const emptyForm = {
  name: '',
  description: '',
  cronExpression: '',
  prompt: '',
  timezone: '',
  taskTitle: '',
};

export default function SchedulerPage() {
  const [tasks, setTasks] = useState<ScheduledTask[]>([]);
  const [loading, setLoading] = useState(true);
  const [showCreate, setShowCreate] = useState(false);
  const [editingTask, setEditingTask] = useState<ScheduledTask | null>(null);
  const [form, setForm] = useState(emptyForm);

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

  const openCreate = () => {
    setForm(emptyForm);
    setEditingTask(null);
    setShowCreate(true);
  };

  const openEdit = (task: ScheduledTask) => {
    setForm({
      name: task.name,
      description: task.description || '',
      cronExpression: task.cronExpression,
      prompt: task.prompt || '',
      timezone: task.timezone || '',
      taskTitle: task.taskTemplate?.title || '',
    });
    setEditingTask(task);
    setShowCreate(true);
  };

  const closeModal = () => {
    setShowCreate(false);
    setEditingTask(null);
    setForm(emptyForm);
  };

  const handleSave = async () => {
    try {
      if (editingTask) {
        await schedulerApi.update(editingTask.id, {
          name: form.name,
          description: form.description || undefined,
          cronExpression: form.cronExpression,
          prompt: form.prompt || undefined,
          timezone: form.timezone || undefined,
        });
      } else {
        await schedulerApi.create({
          name: form.name,
          description: form.description || undefined,
          cronExpression: form.cronExpression,
          taskTitle: form.taskTitle || form.name,
          taskDescription: form.prompt || undefined,
        });
      }
      closeModal();
      loadTasks();
    } catch (error) {
      console.error('Failed to save task:', error);
    }
  };

  const handleToggle = async (task: ScheduledTask) => {
    try {
      if (task.isActive) {
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
      loadTasks();
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
        <Button onClick={openCreate}>
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
              <Card key={task.id} className={!task.isActive ? 'opacity-60' : undefined}>
                <CardContent className="p-4">
                  <div className="flex items-start justify-between">
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-3 mb-2">
                        <h3 className="font-semibold text-lg">{task.name}</h3>
                        {task.isActive ? (
                          <Badge variant="success">Active</Badge>
                        ) : (
                          <Badge variant="secondary">Disabled</Badge>
                        )}
                        {task.timezone && (
                          <Badge variant="outline">{task.timezone}</Badge>
                        )}
                      </div>
                      {task.description && (
                        <p className="text-gray-700 mb-2">{task.description}</p>
                      )}
                      {task.prompt && (
                        <p className="text-gray-500 text-sm mb-2 truncate" title={task.prompt}>
                          Prompt: {task.prompt}
                        </p>
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
                    <div className="flex items-center gap-2 ml-4">
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
                        onClick={() => openEdit(task)}
                        title="Edit"
                      >
                        <Pencil className="h-4 w-4" />
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleToggle(task)}
                        title={task.isActive ? 'Disable' : 'Enable'}
                      >
                        {task.isActive ? (
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

      {/* Create / Edit Task Modal */}
      {showCreate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div className="absolute inset-0 bg-black/50" onClick={closeModal} />
          <div className="relative bg-white rounded-lg shadow-xl w-full max-w-lg mx-4 max-h-[90vh] flex flex-col">
            <div className="flex items-center justify-between p-4 border-b">
              <h2 className="text-lg font-semibold">
                {editingTask ? 'Edit Scheduled Task' : 'Create Scheduled Task'}
              </h2>
              <Button variant="ghost" size="icon" onClick={closeModal}>
                <X className="h-4 w-4" />
              </Button>
            </div>

            <div className="p-4 space-y-4 overflow-auto">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Name <span className="text-red-500">*</span>
                </label>
                <Input
                  value={form.name}
                  onChange={(e) => setForm(prev => ({ ...prev, name: e.target.value }))}
                  placeholder="Morning briefing"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Description
                </label>
                <Textarea
                  value={form.description}
                  onChange={(e) => setForm(prev => ({ ...prev, description: e.target.value }))}
                  placeholder="What does this task do?"
                  rows={2}
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Cron Expression <span className="text-red-500">*</span>
                </label>
                <Input
                  value={form.cronExpression}
                  onChange={(e) => setForm(prev => ({ ...prev, cronExpression: e.target.value }))}
                  placeholder="0 9 * * * (every day at 9 AM UTC)"
                />
                <p className="text-xs text-gray-600 mt-1">
                  Format: minute hour day month weekday (UTC)
                </p>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Prompt
                </label>
                <Textarea
                  value={form.prompt}
                  onChange={(e) => setForm(prev => ({ ...prev, prompt: e.target.value }))}
                  placeholder="Detailed instructions for the assistant when this job runs..."
                  rows={3}
                />
                <p className="text-xs text-gray-600 mt-1">
                  The LLM will execute this prompt with full tool access each time the job fires.
                </p>
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Timezone
                </label>
                <Input
                  value={form.timezone}
                  onChange={(e) => setForm(prev => ({ ...prev, timezone: e.target.value }))}
                  placeholder="Australia/Sydney"
                />
                <p className="text-xs text-gray-600 mt-1">
                  IANA timezone for display. Cron expression must still be in UTC.
                </p>
              </div>

              {!editingTask && (
                <div>
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    Task Title
                  </label>
                  <Input
                    value={form.taskTitle}
                    onChange={(e) => setForm(prev => ({ ...prev, taskTitle: e.target.value }))}
                    placeholder="Defaults to the name"
                  />
                </div>
              )}
            </div>

            <div className="flex items-center justify-end gap-2 p-4 border-t bg-gray-50">
              <Button variant="outline" onClick={closeModal}>
                Cancel
              </Button>
              <Button
                onClick={handleSave}
                disabled={!form.name.trim() || !form.cronExpression.trim()}
              >
                {editingTask ? 'Save Changes' : 'Create Task'}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
