'use client';

import { useDraggable } from '@dnd-kit/core';
import { KanbanTask } from '@/lib/api';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cn, formatRelativeTime, truncate } from '@/lib/utils';
import { Check, X, User, Bot, AlertCircle } from 'lucide-react';

interface KanbanCardProps {
  task: KanbanTask;
  onClick: () => void;
  onApprove?: (taskId: string) => void;
  onReject?: (taskId: string) => void;
  isDragging?: boolean;
}

export function KanbanCard({
  task,
  onClick,
  onApprove,
  onReject,
  isDragging,
}: KanbanCardProps) {
  const { attributes, listeners, setNodeRef, transform } = useDraggable({
    id: task.id,
  });

  const style = transform
    ? {
        transform: `translate3d(${transform.x}px, ${transform.y}px, 0)`,
      }
    : undefined;

  const priorityVariant = {
    Low: 'secondary',
    Normal: 'outline',
    High: 'warning',
    Urgent: 'destructive',
  }[task.priority] as 'secondary' | 'outline' | 'warning' | 'destructive';

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...listeners}
      {...attributes}
      className={cn(
        'bg-white rounded-lg border border-gray-200 p-3 cursor-pointer shadow-sm',
        'hover:border-gray-300 hover:shadow transition-all',
        isDragging && 'opacity-50 shadow-lg rotate-2'
      )}
      onClick={onClick}
    >
      <div className="flex items-start justify-between gap-2 mb-2">
        <h4 className="font-medium text-sm text-gray-900 line-clamp-2">
          {task.title}
        </h4>
        {task.priority !== 'Normal' && (
          <Badge variant={priorityVariant} className="flex-shrink-0">
            {task.priority}
          </Badge>
        )}
      </div>

      {task.description && (
        <p className="text-xs text-gray-600 mb-2 line-clamp-2">
          {truncate(task.description, 100)}
        </p>
      )}

      {task.pendingAction && (
        <div className="mb-2 p-2 bg-yellow-50 rounded border border-yellow-200">
          <div className="flex items-center gap-1 text-xs text-yellow-700">
            <AlertCircle className="h-3 w-3" />
            <span>{task.pendingAction.actionType}</span>
          </div>
          {task.pendingAction.preview && (
            <p className="text-xs text-yellow-600 mt-1 line-clamp-2">
              {task.pendingAction.preview}
            </p>
          )}
        </div>
      )}

      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          {task.assignee === 'User' ? (
            <User className="h-3 w-3 text-blue-500" />
          ) : (
            <Bot className="h-3 w-3 text-purple-500" />
          )}
          <span className="text-xs text-gray-600">
            {formatRelativeTime(task.updatedAt)}
          </span>
        </div>

        {onApprove && onReject && (
          <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
            <Button
              size="sm"
              variant="ghost"
              className="h-7 w-7 p-0 text-green-600 hover:text-green-700 hover:bg-green-50"
              onClick={() => onApprove(task.id)}
            >
              <Check className="h-4 w-4" />
            </Button>
            <Button
              size="sm"
              variant="ghost"
              className="h-7 w-7 p-0 text-red-600 hover:text-red-700 hover:bg-red-50"
              onClick={() => onReject(task.id)}
            >
              <X className="h-4 w-4" />
            </Button>
          </div>
        )}
      </div>
    </div>
  );
}
