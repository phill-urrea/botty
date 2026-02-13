'use client';

import { useDroppable } from '@dnd-kit/core';
import { KanbanCard } from './card';
import { KanbanTask } from '@/lib/api';
import { cn } from '@/lib/utils';

interface KanbanColumnProps {
  id: string;
  title: string;
  color: string;
  tasks: KanbanTask[];
  onTaskClick: (task: KanbanTask) => void;
  onApprove?: (taskId: string) => void;
  onReject?: (taskId: string) => void;
}

export function KanbanColumn({
  id,
  title,
  color,
  tasks,
  onTaskClick,
  onApprove,
  onReject,
}: KanbanColumnProps) {
  const { isOver, setNodeRef } = useDroppable({ id });

  return (
    <div
      ref={setNodeRef}
      className={cn(
        'flex flex-col w-80 rounded-lg',
        color,
        isOver && 'ring-2 ring-blue-500'
      )}
    >
      <div className="p-3 border-b border-gray-200">
        <div className="flex items-center justify-between">
          <h3 className="font-semibold text-gray-900">{title}</h3>
          <span className="text-sm text-gray-600 bg-white px-2 py-0.5 rounded-full">
            {tasks.length}
          </span>
        </div>
      </div>
      <div className="flex-1 p-2 space-y-2 overflow-y-auto min-h-[200px]">
        {tasks.map((task) => (
          <KanbanCard
            key={task.id}
            task={task}
            onClick={() => onTaskClick(task)}
            onApprove={id === 'NeedsApproval' ? onApprove : undefined}
            onReject={id === 'NeedsApproval' ? onReject : undefined}
          />
        ))}
        {tasks.length === 0 && (
          <div className="text-center py-8 text-gray-500 text-sm">
            No tasks
          </div>
        )}
      </div>
    </div>
  );
}
