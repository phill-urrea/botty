'use client';

import { useState } from 'react';
import { KanbanTask, CreateTaskRequest } from '@/lib/api';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Badge } from '@/components/ui/badge';
import { formatDate } from '@/lib/utils';
import { X, Check, Trash2, AlertCircle, User, Bot, Bug } from 'lucide-react';

const TASK_TYPES = [
  { value: 'General', label: 'General' },
  { value: 'BugReport', label: 'Bug Report' },
] as const;

const PRIORITIES = [
  { value: 'Low', label: 'Low' },
  { value: 'Normal', label: 'Normal' },
  { value: 'High', label: 'High' },
  { value: 'Urgent', label: 'Urgent' },
] as const;

interface TaskModalProps {
  task?: KanbanTask;
  onClose: () => void;
  onApprove?: () => void;
  onReject?: (reason?: string) => void;
  onDelete?: () => void;
  onCreate?: (data: CreateTaskRequest) => void;
}

export function TaskModal({
  task,
  onClose,
  onApprove,
  onReject,
  onDelete,
  onCreate,
}: TaskModalProps) {
  const [title, setTitle] = useState(task?.title || '');
  const [description, setDescription] = useState(task?.description || '');
  const [taskType, setTaskType] = useState('General');
  const [priority, setPriority] = useState('Normal');
  const [rejectReason, setRejectReason] = useState('');
  const [showRejectInput, setShowRejectInput] = useState(false);

  const isNew = !task;
  const isBugReport = taskType === 'BugReport';
  const needsApproval = task?.lane === 'NeedsApproval';

  const handleSubmit = () => {
    if (isNew && onCreate) {
      onCreate({
        title,
        description: description || undefined,
        type: taskType,
        assignee: isBugReport ? 'Assistant' : undefined,
        priority,
      });
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-lg shadow-xl w-full max-w-lg mx-4 max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between p-4 border-b">
          <h2 className="text-lg font-semibold">
            {isNew ? (isBugReport ? 'Report Bug' : 'Create Task') : 'Task Details'}
          </h2>
          <Button variant="ghost" size="icon" onClick={onClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        <div className="p-4 space-y-4">
          {isNew ? (
            <>
              <div className="flex gap-3">
                <div className="flex-1">
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    Type
                  </label>
                  <select
                    value={taskType}
                    onChange={(e) => setTaskType(e.target.value)}
                    className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  >
                    {TASK_TYPES.map((t) => (
                      <option key={t.value} value={t.value}>
                        {t.label}
                      </option>
                    ))}
                  </select>
                </div>
                <div className="flex-1">
                  <label className="block text-sm font-medium text-gray-700 mb-1">
                    Priority
                  </label>
                  <select
                    value={priority}
                    onChange={(e) => setPriority(e.target.value)}
                    className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
                  >
                    {PRIORITIES.map((p) => (
                      <option key={p.value} value={p.value}>
                        {p.label}
                      </option>
                    ))}
                  </select>
                </div>
              </div>

              {isBugReport && (
                <div className="p-3 bg-red-50 rounded-lg border border-red-200 text-sm text-red-700">
                  <div className="flex items-center gap-2 font-medium mb-1">
                    <Bug className="h-4 w-4" />
                    Bug Report
                  </div>
                  <p>
                    This will be assigned to the assistant and require your approval before
                    it investigates, fixes, and deploys the change automatically.
                  </p>
                </div>
              )}

              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  {isBugReport ? 'Bug Summary' : 'Title'}
                </label>
                <Input
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  placeholder={isBugReport ? 'Brief summary of the bug' : 'Task title'}
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  {isBugReport ? 'Details & Reproduction Steps' : 'Description'}
                </label>
                <Textarea
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder={
                    isBugReport
                      ? 'Describe the bug, expected vs actual behavior, steps to reproduce, and affected component...'
                      : 'Task description (optional)'
                  }
                  rows={isBugReport ? 6 : 4}
                />
              </div>
            </>
          ) : (
            <>
              <div>
                <h3 className="font-semibold text-lg">{task.title}</h3>
                {task.description && (
                  <p className="text-gray-600 mt-2">{task.description}</p>
                )}
              </div>

              <div className="flex flex-wrap gap-2">
                <Badge variant="outline">{task.type}</Badge>
                <Badge variant={task.priority === 'Urgent' ? 'destructive' : task.priority === 'High' ? 'warning' : 'secondary'}>
                  {task.priority}
                </Badge>
                <Badge variant={task.lane === 'Done' ? 'success' : 'default'}>
                  {task.lane}
                </Badge>
              </div>

              <div className="flex items-center gap-2 text-sm text-gray-600">
                {task.assignee === 'User' ? (
                  <User className="h-4 w-4" />
                ) : (
                  <Bot className="h-4 w-4" />
                )}
                <span>Assigned to {task.assignee}</span>
              </div>

              {task.pendingAction && (
                <div className="p-3 bg-yellow-50 rounded-lg border border-yellow-200">
                  <div className="flex items-center gap-2 text-yellow-800 font-medium">
                    <AlertCircle className="h-4 w-4" />
                    <span>Pending Action: {task.pendingAction.actionType}</span>
                  </div>
                  <p className="text-sm text-yellow-700 mt-1">
                    {task.pendingAction.description}
                  </p>
                  {task.pendingAction.preview && (
                    <div className="mt-2 p-2 bg-white rounded border text-sm">
                      <pre className="whitespace-pre-wrap">{task.pendingAction.preview}</pre>
                    </div>
                  )}
                </div>
              )}

              <div className="text-sm text-gray-600 space-y-1">
                <p>Created: {formatDate(task.createdAt)}</p>
                <p>Updated: {formatDate(task.updatedAt)}</p>
              </div>
            </>
          )}

          {showRejectInput && (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Rejection Reason (optional)
              </label>
              <Textarea
                value={rejectReason}
                onChange={(e) => setRejectReason(e.target.value)}
                placeholder="Why are you rejecting this?"
                rows={2}
              />
            </div>
          )}
        </div>

        <div className="flex items-center justify-between p-4 border-t bg-gray-50">
          {isNew ? (
            <>
              <Button variant="outline" onClick={onClose}>
                Cancel
              </Button>
              <Button onClick={handleSubmit} disabled={!title.trim()}>
                {isBugReport ? 'Submit Bug Report' : 'Create Task'}
              </Button>
            </>
          ) : (
            <>
              <div>
                {onDelete && (
                  <Button variant="destructive" size="sm" onClick={onDelete}>
                    <Trash2 className="h-4 w-4 mr-2" />
                    Delete
                  </Button>
                )}
              </div>
              <div className="flex items-center gap-2">
                {needsApproval && onApprove && onReject && (
                  showRejectInput ? (
                    <>
                      <Button variant="outline" onClick={() => setShowRejectInput(false)}>
                        Cancel
                      </Button>
                      <Button
                        variant="destructive"
                        onClick={() => onReject(rejectReason || undefined)}
                      >
                        Confirm Reject
                      </Button>
                    </>
                  ) : (
                    <>
                      <Button
                        variant="outline"
                        onClick={() => setShowRejectInput(true)}
                      >
                        <X className="h-4 w-4 mr-2" />
                        Reject
                      </Button>
                      <Button onClick={onApprove}>
                        <Check className="h-4 w-4 mr-2" />
                        Approve
                      </Button>
                    </>
                  )
                )}
                {!needsApproval && (
                  <Button variant="outline" onClick={onClose}>
                    Close
                  </Button>
                )}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
