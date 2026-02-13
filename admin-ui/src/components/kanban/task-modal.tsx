'use client';

import { useState } from 'react';
import { KanbanTask } from '@/lib/api';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Badge } from '@/components/ui/badge';
import { formatDate } from '@/lib/utils';
import { X, Check, Trash2, AlertCircle, User, Bot } from 'lucide-react';

interface TaskModalProps {
  task?: KanbanTask;
  onClose: () => void;
  onApprove?: () => void;
  onReject?: (reason?: string) => void;
  onDelete?: () => void;
  onCreate?: (data: { title: string; description?: string }) => void;
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
  const [rejectReason, setRejectReason] = useState('');
  const [showRejectInput, setShowRejectInput] = useState(false);

  const isNew = !task;
  const needsApproval = task?.lane === 'NeedsApproval';

  const handleSubmit = () => {
    if (isNew && onCreate) {
      onCreate({ title, description: description || undefined });
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white rounded-lg shadow-xl w-full max-w-lg mx-4 max-h-[90vh] overflow-y-auto">
        <div className="flex items-center justify-between p-4 border-b">
          <h2 className="text-lg font-semibold">
            {isNew ? 'Create Task' : 'Task Details'}
          </h2>
          <Button variant="ghost" size="icon" onClick={onClose}>
            <X className="h-4 w-4" />
          </Button>
        </div>

        <div className="p-4 space-y-4">
          {isNew ? (
            <>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Title
                </label>
                <Input
                  value={title}
                  onChange={(e) => setTitle(e.target.value)}
                  placeholder="Task title"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Description
                </label>
                <Textarea
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Task description (optional)"
                  rows={4}
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
                Create Task
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
